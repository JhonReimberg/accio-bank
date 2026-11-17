using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Credit;
using Bank.Domain.Investments;
using Bank.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Investments;

public record OpenInvestmentRequest(InvestmentType Type, string Name, decimal? Goal);

public record MoveInvestmentRequest(decimal Amount, string Pin);

public record RoundUpRequest(bool Enabled);

public record InvestmentResponse(
    Guid Id, InvestmentType Type, string Name, decimal? Goal, decimal CdiPercent, decimal Principal, decimal Value, decimal Income,
    decimal BlockedAmount, decimal AvailableToRedeem, bool RoundUpCardPurchases, decimal? GoalProgress, DateTime CreatedAt);

public record RedemptionResponse(Guid TransactionId, decimal Gross, decimal Income, decimal Iof, decimal IncomeTax, decimal Net);

public record RedemptionPreview(decimal Gross, decimal Income, decimal Iof, decimal IncomeTax, decimal Net);

public record ContractGuaranteedLoanRequest(Guid InvestmentId, decimal Amount, int Installments, string Pin);

public class InvestmentService(
    IBankDbContext db,
    ICurrentUser currentUser,
    LedgerPosting ledger,
    TransactionAuthorizer authorizer,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>CDB with daily liquidity pays 100% of CDI; caixinhas too (same product, with a goal).</summary>
    public const decimal DefaultCdiPercent = 1m;

    public async Task<IReadOnlyList<InvestmentResponse>> ListAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var list = await db.Investments.Include(i => i.Lots).AsNoTracking().Where(i => i.AccountId == accountId)
            .OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return list.Select(ToResponse).ToList();
    }

    public async Task<InvestmentResponse> OpenAsync(OpenInvestmentRequest req, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        if (req.Type == InvestmentType.Cdb && await db.Investments.AnyAsync(i => i.AccountId == accountId && i.Type == InvestmentType.Cdb, ct))
            throw new ConflictException("Você já tem um CDB de liquidez diária. Aplique nele.");
        var investment = Investment.Open(accountId, req.Type, req.Type == InvestmentType.Cdb ? "CDB Liquidez Diária" : req.Name,
            req.Goal, DefaultCdiPercent, clock.GetUtcNow().UtcDateTime);
        db.Investments.Add(investment);
        await db.SaveChangesAsync(ct);
        return ToResponse(investment);
    }

    public async Task<InvestmentResponse> ApplyAsync(Guid investmentId, MoveInvestmentRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var accountId = await MyAccountIdAsync(ct);
        var investment = await ApplyCoreAsync(accountId, investmentId, req.Amount, "Aplicação", ct);
        return ToResponse(investment);
    }

    /// <summary>Moves money from the checking account into a position (also used by card round-ups).</summary>
    public async Task<Investment> ApplyCoreAsync(Guid accountId, Guid investmentId, decimal amount, string description, CancellationToken ct)
    {
        Investment? investment = null;
        await ledger.ExecuteAsync([accountId, SystemAccounts.InvestmentCustody], async (accounts, now) =>
        {
            investment = await db.Investments.Include(i => i.Lots).SingleOrDefaultAsync(i => i.Id == investmentId && i.AccountId == accountId, ct)
                ?? throw new NotFoundException("Investimento não encontrado.");
            investment.Apply(amount, now);
            audit.Record("investment.applied", nameof(Investment), investment.Id, after: new { amount });
            return LedgerTransaction.Transfer(TransactionKind.InvestmentApplication, accounts[accountId], accounts[SystemAccounts.InvestmentCustody],
                amount, $"{description}: {investment.Name}", $"Custódia: {investment.Name}", now);
        }, ct);
        return investment!;
    }

    public async Task<RedemptionPreview> PreviewRedemptionAsync(Guid investmentId, decimal amount, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        // Simulated on a detached copy: nothing is saved.
        var investment = await db.Investments.Include(i => i.Lots).AsNoTracking().SingleOrDefaultAsync(i => i.Id == investmentId && i.AccountId == accountId, ct)
            ?? throw new NotFoundException("Investimento não encontrado.");
        var r = investment.Redeem(amount, clock.GetUtcNow().UtcDateTime);
        return new RedemptionPreview(r.Gross, r.Income, r.Iof, r.IncomeTax, r.Net);
    }

    /// <summary>
    /// Redemption: principal comes back from custody, income is the bank's expense, and IOF/IR are withheld
    /// to the payable accounts — a five-leg, balanced posting.
    /// </summary>
    public async Task<RedemptionResponse> RedeemAsync(Guid investmentId, MoveInvestmentRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var accountId = await MyAccountIdAsync(ct);
        RedemptionBreakdown? r = null;
        var tx = await ledger.ExecuteAsync(
            [accountId, SystemAccounts.InvestmentCustody, SystemAccounts.InvestmentYieldExpense, SystemAccounts.IofPayable, SystemAccounts.IncomeTaxPayable],
            async (accounts, now) =>
            {
                var investment = await db.Investments.Include(i => i.Lots).SingleOrDefaultAsync(i => i.Id == investmentId && i.AccountId == accountId, ct)
                    ?? throw new NotFoundException("Investimento não encontrado.");
                r = investment.Redeem(req.Amount, now);
                var postings = new List<Posting>
                {
                    new(accounts[SystemAccounts.InvestmentCustody], EntryDirection.Debit, r.Principal, $"Resgate: {investment.Name}"),
                    new(accounts[accountId], EntryDirection.Credit, r.Net, $"Resgate: {investment.Name}"),
                };
                if (r.Income > 0) postings.Add(new(accounts[SystemAccounts.InvestmentYieldExpense], EntryDirection.Debit, r.Income, "Rendimento pago"));
                if (r.Iof > 0) postings.Add(new(accounts[SystemAccounts.IofPayable], EntryDirection.Credit, r.Iof, "IOF sobre resgate"));
                if (r.IncomeTax > 0) postings.Add(new(accounts[SystemAccounts.IncomeTaxPayable], EntryDirection.Credit, r.IncomeTax, "IR retido sobre rendimento"));
                audit.Record("investment.redeemed", nameof(Investment), investment.Id, after: r);
                return LedgerTransaction.Post(TransactionKind.InvestmentRedemption, now, postings);
            }, ct);
        return new RedemptionResponse(tx.Id, r!.Gross, r.Income, r.Iof, r.IncomeTax, r.Net);
    }

    public async Task<InvestmentResponse> SetRoundUpAsync(Guid investmentId, RoundUpRequest req, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var investment = await db.Investments.Include(i => i.Lots).SingleOrDefaultAsync(i => i.Id == investmentId && i.AccountId == accountId, ct)
            ?? throw new NotFoundException("Investimento não encontrado.");
        // Only one caixinha receives round-ups at a time.
        if (req.Enabled)
            await db.Investments.Where(i => i.AccountId == accountId && i.Id != investmentId && i.RoundUpCardPurchases)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.RoundUpCardPurchases, false), ct);
        investment.SetRoundUp(req.Enabled);
        await db.SaveChangesAsync(ct);
        return ToResponse(investment);
    }

    private static InvestmentResponse ToResponse(Investment i) => new(
        i.Id, i.Type, i.Name, i.Goal, i.CdiPercent, i.Principal, i.Value, i.Income, i.BlockedAmount, i.AvailableToRedeem,
        i.RoundUpCardPurchases, i.Goal is > 0 ? Math.Min(1, i.Value / i.Goal.Value) : null, i.CreatedAt);

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("Conta não encontrada.");
    }
}

/// <summary>Business days only (cron Mon–Fri): every position earns one day of its % of CDI.</summary>
public class AccrueInvestmentYieldJob(IBankDbContext db, CreditData creditData, ILogger<AccrueInvestmentYieldJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var policy = await creditData.PolicyAsync(ct);
        var daily = InvestmentTax.DailyRate(policy.CdiAnnualRate, 1m);
        var investments = await db.Investments.Include(i => i.Lots).Where(i => i.Lots.Any()).ToListAsync(ct);
        var total = investments.Sum(i => i.AccrueDailyYield(daily));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Investment yield accrued: {Total} on {Count} positions (CDI {Cdi})", total, investments.Count, policy.CdiAnnualRate);
    }
}

/// <summary>Loan backed by an investment: lower rate, the same value is blocked in the investment until paid off.</summary>
public class GuaranteedLoanService(
    IBankDbContext db,
    ICurrentUser currentUser,
    CreditData creditData,
    LedgerPosting ledger,
    TransactionAuthorizer authorizer,
    IAuditLog audit,
    TimeProvider clock)
{
    public async Task<LoanResponse> ContractAsync(ContractGuaranteedLoanRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var profile = await creditData.ProfileAsync(currentUser.UserId, ct);
        var policy = profile.Policy;
        if (profile.Account.Status != AccountStatus.Active)
            throw new DomainException("credit.not_eligible", "Sua conta precisa estar ativa.");
        if (req.Installments < policy.MinInstallments || req.Installments > policy.MaxInstallments)
            throw new DomainException("credit.installments", $"Escolha entre {policy.MinInstallments} e {policy.MaxInstallments} parcelas.");
        var today = DateOnly.FromDateTime(BankTime.ToLocal(clock.GetUtcNow().UtcDateTime));
        var quote = LoanMath.Quote(req.Amount, req.Installments, policy.GuaranteedMonthlyRate, today, new IofRates());
        var income = profile.Customer.MonthlyIncome ?? 0;
        if (profile.MonthlyCommitment + quote.InstallmentAmount > income * policy.MaxIncomeCommitment)
            throw new DomainException("credit.income_commitment", $"As parcelas passariam de {Formatting.Percent(policy.MaxIncomeCommitment)} da sua renda.");

        var accountId = profile.Account.Id;
        Loan? loan = null;
        await ledger.ExecuteAsync([accountId, SystemAccounts.LoanPortfolio, SystemAccounts.IofPayable], async (accounts, now) =>
        {
            var investment = await db.Investments.Include(i => i.Lots).SingleOrDefaultAsync(i => i.Id == req.InvestmentId && i.AccountId == accountId, ct)
                ?? throw new NotFoundException("Investimento não encontrado.");
            investment.BlockAsCollateral(req.Amount);
            var postings = new List<Posting>
            {
                new(accounts[SystemAccounts.LoanPortfolio], EntryDirection.Debit, quote.RequestedAmount, "Empréstimo com garantia concedido"),
                new(accounts[accountId], EntryDirection.Credit, quote.NetAmount, $"Empréstimo com garantia ({quote.Installments}x)"),
            };
            if (quote.Iof > 0) postings.Add(new(accounts[SystemAccounts.IofPayable], EntryDirection.Credit, quote.Iof, "IOF sobre empréstimo"));
            var tx = LedgerTransaction.Post(TransactionKind.LoanDisbursement, now, postings);
            loan = Loan.Contract(accountId, profile.Customer.UserId, quote, profile.Score ?? 0, tx.Id, now, guaranteeInvestmentId: investment.Id);
            db.Loans.Add(loan);
            audit.Record("credit.guaranteed_loan_contracted", nameof(Loan), loan.Id,
                after: new { quote.RequestedAmount, quote.Installments, quote.MonthlyRate, Investment = investment.Id });
            return tx;
        }, ct);
        return new LoanResponse(loan!.Id, loan.Amount, loan.NetAmount, loan.Iof, loan.MonthlyRate, loan.CetMonthly, loan.CetYearly,
            loan.InstallmentCount, loan.InstallmentAmount, loan.Status, loan.OutstandingPrincipal, loan.ContractedAt, []);
    }
}

/// <summary>When a guaranteed loan is paid off, the collateral is released.</summary>
public class ReleaseCollateralHandler(IBankDbContext db) : IDomainEventHandler<LoanPaidOff>
{
    public async Task HandleAsync(LoanPaidOff e, CancellationToken ct)
    {
        var loan = await db.Loans.AsNoTracking().SingleAsync(l => l.Id == e.LoanId, ct);
        if (loan.GuaranteeInvestmentId is not { } investmentId) return;
        var investment = await db.Investments.SingleAsync(i => i.Id == investmentId, ct);
        investment.ReleaseCollateral(loan.Amount);
        await db.SaveChangesAsync(ct);
    }
}
