using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Credit;
using Bank.Domain.Customers;
using Bank.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Credit;

/// <summary>Everything credit decisions look at for one customer, loaded once per request.</summary>
public sealed record CreditProfile(
    Customer Customer,
    Account Account,
    CreditPolicy Policy,
    int? Score,
    decimal Credits30Days,
    decimal MonthlyCommitment,
    int OverdueInstallments,
    DateTime? NegativeSince,
    DateTime Now);

/// <summary>Shared queries for score, eligibility and loans.</summary>
public class CreditData(IBankDbContext db, TimeProvider clock)
{
    /// <summary>Transactions that do not count as "movement" for credit (the loan itself, charges).</summary>
    private static readonly TransactionKind[] NotMovement = [TransactionKind.LoanDisbursement, TransactionKind.OverdraftInterest];

    public async Task<CreditPolicy> PolicyAsync(CancellationToken ct) =>
        await db.CreditPolicies.SingleOrDefaultAsync(ct) ?? new CreditPolicy();

    public async Task<CreditProfile> ProfileAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new UnauthorizedException("Cadastro não encontrado.");
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == userId, ct);
        var policy = await PolicyAsync(ct);
        var score = await db.CreditScores.Where(s => s.UserId == userId).OrderByDescending(s => s.CalculatedAt)
            .Select(s => (int?)s.Score).FirstOrDefaultAsync(ct);
        var credits30 = await CreditsSinceAsync(account.Id, now.AddDays(-30), ct);
        var commitment = await db.Loans.Where(l => l.UserId == userId && l.Status == LoanStatus.Active)
            .SumAsync(l => (decimal?)l.InstallmentAmount, ct) ?? 0;
        var overdue = await db.LoanInstallments.CountAsync(i =>
            i.Status == InstallmentStatus.Overdue && db.Loans.Any(l => l.Id == i.LoanId && l.UserId == userId), ct);
        return new CreditProfile(customer, account, policy, score, credits30, commitment, overdue,
            await NegativeSinceAsync(account, ct), now);
    }

    /// <summary>Money received in the period, excluding loan disbursements (borrowing is not income).</summary>
    public Task<decimal> CreditsSinceAsync(Guid accountId, DateTime since, CancellationToken ct) =>
        (from e in db.LedgerEntries
         join t in db.LedgerTransactions on e.TransactionId equals t.Id
         where e.AccountId == accountId && e.Direction == EntryDirection.Credit && e.CreatedAt >= since && !NotMovement.Contains(t.Kind)
         select e.Amount).SumAsync(ct);

    /// <summary>When the balance last went below zero and stayed there (null when it is not negative now).</summary>
    public async Task<DateTime?> NegativeSinceAsync(Account account, CancellationToken ct)
    {
        if (account.Balance >= 0) return null;
        var lastNonNegative = await db.LedgerEntries
            .Where(e => e.AccountId == account.Id && e.BalanceAfter >= 0)
            .OrderByDescending(e => e.CreatedAt).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync(ct);
        return await db.LedgerEntries
            .Where(e => e.AccountId == account.Id && e.BalanceAfter < 0 && (lastNonNegative == null || e.CreatedAt > lastNonNegative))
            .OrderBy(e => e.CreatedAt).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync(ct);
    }
}
