using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Domain.Accounts;
using Bank.Domain.Credit;
using Bank.Domain.Customers;
using Bank.Domain.Ledger;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Admin;

public record CustomerListItem(Guid UserId, string Name, string Email, string? MaskedCpf, AccountStatus? AccountStatus, AccountTier? Tier, KycStatus KycStatus, int? Score);

public record Customer360(
    Guid UserId, Guid? AccountId, string Name, string Email, string? Cpf, string? Phone, DateOnly? BirthDate, Address? Address,
    decimal? MonthlyIncome, string? Occupation, KycStatus KycStatus, DateTime CreatedAt,
    AccountSummary? Account, ScoreResponse? Score, IReadOnlyList<LoanSummary> Loans, IReadOnlyList<RecentTransaction> RecentTransactions);

public record AccountSummary(
    string Number, AccountStatus Status, string? StatusReason, AccountTier Tier, decimal Balance, decimal BlockedBalance,
    decimal OverdraftLimit, decimal OverdraftUsed, DateTime? ActivatedAt);

public record LoanSummary(Guid Id, decimal Amount, int InstallmentCount, int PaidInstallments, int OverdueInstallments, decimal OutstandingPrincipal, LoanStatus Status, DateTime ContractedAt);

public record RecentTransaction(Guid TransactionId, TransactionKind Kind, decimal Amount, string Description, DateTime CreatedAt);

public record SetOverdraftLimitRequest(decimal Limit, string Reason);

public record DashboardResponse(
    int Customers, int ActiveAccounts, int PendingKyc,
    decimal PixVolume30Days, int PixCount30Days, IReadOnlyList<DailyVolume> PixByDay,
    decimal CreditPortfolio, int ActiveLoans, decimal OverdueAmount, int OverdueInstallments, decimal DelinquencyRate,
    decimal OverdraftUsed, bool LedgerBalanced);

public record DailyVolume(DateOnly Day, decimal Amount, int Count);

/// <summary>Back-office (Analyst/Admin): customer 360° view, credit policy, limits and the dashboard.</summary>
public class BackofficeService(
    IBankDbContext db,
    ICurrentUser currentUser,
    ScoreService scores,
    CreditData creditData,
    Ledger.ReconciliationService reconciliation,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<CustomerListItem>> SearchCustomersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query =
            from c in db.Customers
            join u in db.Users on c.UserId equals u.Id
            join a in db.Accounts on u.Id equals a.UserId into accounts
            from a in accounts.DefaultIfEmpty()
            select new { c, u, a };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            query = query.Where(x => x.u.Email.Contains(term) || (x.c.FullName ?? x.u.Name).ToLower().Contains(term)
                                     || (digits.Length >= 3 && x.c.Cpf != null && x.c.Cpf.Contains(digits)));
        }
        var result = await query.OrderBy(x => x.c.FullName ?? x.u.Name)
            .Select(x => new CustomerListItem(
                x.u.Id, x.c.FullName ?? x.u.Name, x.u.Email, x.c.Cpf, x.a != null ? x.a.Status : null, x.a != null ? x.a.Tier : null, x.c.KycStatus,
                db.CreditScores.Where(s => s.UserId == x.u.Id).OrderByDescending(s => s.CalculatedAt).Select(s => (int?)s.Score).FirstOrDefault()))
            .ToPagedAsync(page, pageSize, ct);
        return result with { Items = result.Items.Select(i => i with { MaskedCpf = i.MaskedCpf is null ? null : Cpf.Mask(i.MaskedCpf) }).ToList() };
    }

    /// <summary>Everything about one customer in one screen. Viewing it is audited (it shows the full CPF).</summary>
    public async Task<Customer360> GetCustomerAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.Role == Role.Customer, ct)
            ?? throw new NotFoundException("Cliente não encontrado.");
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.UserId == userId, ct);
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, ct);
        var score = customer.KycStatus == KycStatus.Approved ? await scores.GetAsync(userId, ct) : null;

        var loans = await db.Loans.AsNoTracking().Where(l => l.UserId == userId).OrderByDescending(l => l.ContractedAt)
            .Select(l => new LoanSummary(l.Id, l.Amount, l.InstallmentCount,
                l.Installments.Count(i => i.Status == InstallmentStatus.Paid),
                l.Installments.Count(i => i.Status == InstallmentStatus.Overdue),
                l.Installments.Where(i => i.Status != InstallmentStatus.Paid).Sum(i => i.Principal),
                l.Status, l.ContractedAt))
            .ToListAsync(ct);

        var recent = account is null ? [] : await (
                from e in db.LedgerEntries
                join t in db.LedgerTransactions on e.TransactionId equals t.Id
                where e.AccountId == account.Id
                orderby e.CreatedAt descending
                select new RecentTransaction(t.Id, t.Kind, e.Direction == EntryDirection.Credit ? e.Amount : -e.Amount, e.Description, e.CreatedAt))
            .Take(15).ToListAsync(ct);

        audit.Record("backoffice.customer_viewed", nameof(Customer), customer.Id);
        await db.SaveChangesAsync(ct);

        return new Customer360(
            userId, account?.Id, customer.FullName ?? user.Name, user.Email, customer.Cpf is null ? null : Cpf.Format(customer.Cpf),
            customer.Phone, customer.BirthDate, customer.Address, customer.MonthlyIncome, customer.Occupation, customer.KycStatus, user.CreatedAt,
            account is null ? null : new AccountSummary(account.Number.ToString(), account.Status, account.StatusReason, account.Tier, account.Balance,
                account.BlockedBalance, account.OverdraftLimit, account.OverdraftUsed, account.ActivatedAt),
            score, loans, recent);
    }

    public async Task SetOverdraftLimitAsync(Guid accountId, SetOverdraftLimitRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new Domain.Common.DomainException("reason.required", "Informe o motivo do ajuste.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).SingleOrDefault() ?? throw new NotFoundException("Conta não encontrada.");
        var before = account.OverdraftLimit;
        account.SetOverdraftLimit(req.Limit);
        audit.Record("backoffice.overdraft_limit_changed", nameof(Account), account.Id, new { Limit = before }, new { req.Limit, req.Reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public Task<CreditPolicy> GetPolicyAsync(CancellationToken ct) => creditData.PolicyAsync(ct);

    public async Task<CreditPolicy> UpdatePolicyAsync(CreditPolicyValues values, CancellationToken ct)
    {
        var policy = await db.CreditPolicies.SingleOrDefaultAsync(ct);
        if (policy is null)
        {
            policy = new CreditPolicy();
            db.CreditPolicies.Add(policy);
        }
        var before = JsonSerializer.Serialize(policy, Json);
        policy.Update(values, currentUser.UserId, clock.GetUtcNow().UtcDateTime);
        audit.Record("backoffice.credit_policy_updated", nameof(CreditPolicy), policy.Id,
            JsonDocument.Parse(before).RootElement, policy);
        await db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task<DashboardResponse> DashboardAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-30);
        var pix = await db.PixTransfers.Where(p => p.CreatedAt >= since).Select(p => new { p.Amount, p.CreatedAt }).ToListAsync(ct);
        var byDay = pix.GroupBy(p => DateOnly.FromDateTime(BankTime.ToLocal(p.CreatedAt)))
            .Select(g => new DailyVolume(g.Key, g.Sum(p => p.Amount), g.Count())).OrderBy(d => d.Day).ToList();

        var unpaid = db.LoanInstallments.Where(i => i.Status != InstallmentStatus.Paid);
        var portfolio = await unpaid.SumAsync(i => (decimal?)i.Principal, ct) ?? 0;
        var overdue = db.LoanInstallments.Where(i => i.Status == InstallmentStatus.Overdue);
        var overdueAmount = await overdue.SumAsync(i => (decimal?)i.Amount, ct) ?? 0;
        var overduePrincipal = await overdue.SumAsync(i => (decimal?)i.Principal, ct) ?? 0;

        return new DashboardResponse(
            await db.Customers.CountAsync(ct),
            await db.Accounts.CountAsync(a => a.Type == AccountType.Customer && a.Status == AccountStatus.Active, ct),
            await db.Customers.CountAsync(c => c.KycStatus == KycStatus.PendingReview, ct),
            pix.Sum(p => p.Amount), pix.Count, byDay,
            portfolio,
            await db.Loans.CountAsync(l => l.Status == LoanStatus.Active, ct),
            overdueAmount,
            await overdue.CountAsync(ct),
            // Share of the outstanding principal that is in overdue installments.
            portfolio == 0 ? 0 : Math.Round(overduePrincipal / portfolio, 4),
            await db.Accounts.Where(a => a.Type == AccountType.Customer && a.Balance < 0).SumAsync(a => (decimal?)-a.Balance, ct) ?? 0,
            (await reconciliation.RunAsync(ct)).IsBalanced);
    }
}
