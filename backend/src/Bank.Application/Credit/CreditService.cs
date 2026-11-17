using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Credit;
using Bank.Domain.Ledger;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Credit;

public record LoanOffer(decimal MaxAmount, decimal MonthlyRate, int MinInstallments, int MaxInstallments, decimal MinAmount, decimal MaxInstallmentAmount);

public record EligibilityResponse(bool Eligible, int? Score, IReadOnlyList<RuleResult> Rules, LoanOffer? Offer);

public record SimulateLoanRequest(decimal Amount, int Installments);

public record ContractLoanRequest(decimal Amount, int Installments, string Pin);

public record PayInstallmentRequest(string Pin);

public record InstallmentResponse(
    int Number, DateOnly DueDate, decimal Amount, decimal Interest, decimal Principal, decimal BalanceAfter,
    InstallmentStatus Status, decimal? PaidAmount, DateTime? PaidAt, decimal? AmountDueToday);

public record LoanResponse(
    Guid Id, decimal Amount, decimal NetAmount, decimal Iof, decimal MonthlyRate, decimal CetMonthly, decimal CetYearly,
    int InstallmentCount, decimal InstallmentAmount, LoanStatus Status, decimal OutstandingPrincipal, DateTime ContractedAt,
    IReadOnlyList<InstallmentResponse> Installments);

public record OverdraftResponse(decimal Limit, decimal Used, decimal MaxLimitOffered, decimal MonthlyRate);

public record SetOverdraftRequest(decimal Limit, string Pin);

public interface ILoanContractRenderer
{
    byte[] RenderPdf(Loan loan, string customerName, string cpf, string accountNumber);
}

public class ContractLoanRequestValidator : AbstractValidator<ContractLoanRequest>
{
    public ContractLoanRequestValidator()
    {
        RuleFor(x => x.Amount).MoneyAmount(max: 1_000_000).WithName("Valor");
        RuleFor(x => x.Pin).NotEmpty().WithName("Senha transacional");
    }
}

/// <summary>Customer side of credit: eligibility, simulation, personal loans, installments and overdraft.</summary>
public class CreditService(
    IBankDbContext db,
    ICurrentUser currentUser,
    CreditData data,
    ScoreService scores,
    IEnumerable<IEligibilityRule> rules,
    EligibilityContext eligibility,
    LedgerPosting ledger,
    TransactionAuthorizer authorizer,
    IAuditLog audit,
    ILoanContractRenderer contracts,
    TimeProvider clock,
    IValidator<ContractLoanRequest> contractValidator)
{
    private static readonly IofRates Iof = new();

    public async Task<EligibilityResponse> GetEligibilityAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        await EnsureScoreAsync(userId, ct);
        var profile = await data.ProfileAsync(userId, ct);
        eligibility.Profile = profile;
        var results = new List<RuleResult>();
        foreach (var rule in rules) results.Add(await rule.EvaluateAsync(profile.Customer, ct));
        var eligible = results.All(r => r.Passed);
        return new EligibilityResponse(eligible, profile.Score, results, eligible ? await OfferAsync(profile, ct) : null);
    }

    public async Task<LoanQuote> SimulateAsync(SimulateLoanRequest req, CancellationToken ct)
    {
        var (profile, offer) = await RequireEligibleAsync(ct);
        return Quote(profile, offer, req.Amount, req.Installments);
    }

    public async Task<LoanResponse> ContractAsync(ContractLoanRequest req, CancellationToken ct)
    {
        await contractValidator.ValidateAndThrowAsync(req, ct);
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var (profile, offer) = await RequireEligibleAsync(ct);
        var quote = Quote(profile, offer, req.Amount, req.Installments);

        Loan? loan = null;
        var accountId = profile.Account.Id;
        // shortcut: every contract locks the portfolio and IOF rows; shard those system accounts if volume grows.
        await ledger.ExecuteAsync([accountId, SystemAccounts.LoanPortfolio, SystemAccounts.IofPayable], (accounts, now) =>
        {
            // Multi-leg posting: the portfolio lends the full amount, the customer receives it net of IOF.
            var postings = new List<Posting>
            {
                new(accounts[SystemAccounts.LoanPortfolio], EntryDirection.Debit, quote.RequestedAmount, "Empréstimo concedido"),
                new(accounts[accountId], EntryDirection.Credit, quote.NetAmount, $"Empréstimo pessoal ({quote.Installments}x)"),
            };
            if (quote.Iof > 0) postings.Add(new(accounts[SystemAccounts.IofPayable], EntryDirection.Credit, quote.Iof, "IOF sobre empréstimo"));
            var tx = LedgerTransaction.Post(TransactionKind.LoanDisbursement, now, postings);
            loan = Loan.Contract(accountId, profile.Customer.UserId, quote, profile.Score ?? 0, tx.Id, now);
            db.Loans.Add(loan);
            audit.Record("credit.loan_contracted", nameof(Loan), loan.Id,
                after: new { quote.RequestedAmount, quote.Installments, quote.MonthlyRate, quote.Iof, quote.CetYearly, profile.Score });
            return Task.FromResult(tx);
        }, ct);
        return ToResponse(loan!, Today(), profile.Policy);
    }

    public async Task<IReadOnlyList<LoanResponse>> ListLoansAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var policy = await data.PolicyAsync(ct);
        var loans = await db.Loans.Include(l => l.Installments).AsNoTracking()
            .Where(l => l.UserId == userId).OrderByDescending(l => l.ContractedAt).ToListAsync(ct);
        return loans.Select(l => ToResponse(l, Today(), policy)).ToList();
    }

    /// <summary>Pays an installment now: overdue ones with fine and default interest, future ones with the early-payment discount.</summary>
    public async Task<LoanResponse> PayInstallmentAsync(Guid loanId, int number, PayInstallmentRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var userId = currentUser.UserId;
        var accountId = await db.Loans.Where(l => l.Id == loanId && l.UserId == userId).Select(l => (Guid?)l.AccountId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Empréstimo não encontrado.");
        var policy = await data.PolicyAsync(ct);
        var loan = await PayAsync(accountId, loanId, number, policy, bySystem: false, ct);
        return ToResponse(loan, Today(), policy);
    }

    /// <summary>Shared by the customer (pay now) and the collection job (automatic debit).</summary>
    public async Task<Loan> PayAsync(Guid accountId, Guid loanId, int number, CreditPolicy policy, bool bySystem, CancellationToken ct)
    {
        Loan? loan = null;
        await ledger.ExecuteAsync([accountId, SystemAccounts.LoanPortfolio, SystemAccounts.CreditRevenue], async (accounts, now) =>
        {
            // Re-read under the account lock: two payments of the same installment cannot both go through.
            loan = await db.Loans.Include(l => l.Installments).SingleAsync(l => l.Id == loanId, ct);
            var installment = loan.Installment(number);
            var today = DateOnly.FromDateTime(BankTime.ToLocal(now));
            var charge = installment.ChargeOn(today, loan.MonthlyRate, policy.LateFeeRate, policy.DailyLateInterestRate);

            var postings = new List<Posting>
            {
                new(accounts[accountId], EntryDirection.Debit, charge.Total, $"Parcela {number}/{loan.InstallmentCount} do empréstimo"),
                new(accounts[SystemAccounts.LoanPortfolio], EntryDirection.Credit, charge.Principal, "Amortização de empréstimo"),
            };
            if (charge.Revenue > 0) postings.Add(new(accounts[SystemAccounts.CreditRevenue], EntryDirection.Credit, charge.Revenue, "Juros e encargos de empréstimo"));
            var tx = LedgerTransaction.Post(TransactionKind.LoanPayment, now, postings);
            loan.RegisterPayment(installment, charge, tx.Id, today, now);
            audit.Record("credit.installment_paid", nameof(Loan), loan.Id,
                after: new { number, charge.Total, charge.LateFee, charge.LateInterest, charge.Discount }, bySystem: bySystem);
            return tx;
        }, ct);
        return loan!;
    }

    public async Task<byte[]> GetContractPdfAsync(Guid loanId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var loan = await db.Loans.Include(l => l.Installments).AsNoTracking().SingleOrDefaultAsync(l => l.Id == loanId && l.UserId == userId, ct)
            ?? throw new NotFoundException("Empréstimo não encontrado.");
        var holder = await (
                from c in db.Customers
                join a in db.Accounts on c.UserId equals a.UserId
                where c.UserId == userId
                select new { c.FullName, c.Cpf, a.Number })
            .SingleAsync(ct);
        return contracts.RenderPdf(loan, holder.FullName!, Domain.Customers.Cpf.Format(holder.Cpf!), holder.Number.ToString());
    }

    // --- Overdraft (cheque especial) ---

    public async Task<OverdraftResponse> GetOverdraftAsync(CancellationToken ct)
    {
        await EnsureScoreAsync(currentUser.UserId, ct);
        var profile = await data.ProfileAsync(currentUser.UserId, ct);
        return new OverdraftResponse(profile.Account.OverdraftLimit, profile.Account.OverdraftUsed, MaxOverdraft(profile), profile.Policy.OverdraftMonthlyRate);
    }

    /// <summary>The customer turns the overdraft on, raises it up to the offer for their score, or lowers it.</summary>
    public async Task<OverdraftResponse> SetOverdraftAsync(SetOverdraftRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var profile = await data.ProfileAsync(currentUser.UserId, ct);
        var max = MaxOverdraft(profile);
        if (req.Limit > max)
            throw new DomainException("overdraft.above_offer", $"O limite disponível para você é de até {Formatting.Brl(max)}.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([profile.Account.Id], ct)).Single();
        var before = account.OverdraftLimit;
        account.SetOverdraftLimit(req.Limit);
        audit.Record("credit.overdraft_limit_changed", nameof(Account), account.Id, new { Limit = before }, new { Limit = req.Limit });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new OverdraftResponse(account.OverdraftLimit, account.OverdraftUsed, max, profile.Policy.OverdraftMonthlyRate);
    }

    private static decimal MaxOverdraft(CreditProfile p) =>
        p.Account.Status == AccountStatus.Active && p.OverdueInstallments == 0 && p.Score is { } score
            ? p.Policy.BandFor(score)?.OverdraftLimit ?? 0
            : 0;

    // --- helpers ---

    /// <summary>New customers get a score on their first credit visit instead of waiting for the nightly job.</summary>
    private async Task EnsureScoreAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.CreditScores.AnyAsync(s => s.UserId == userId, ct)
            && await db.Customers.AnyAsync(c => c.UserId == userId && c.KycStatus == Domain.Customers.KycStatus.Approved, ct))
            await scores.RecalculateAsync(userId, ct);
    }

    private async Task<(CreditProfile Profile, LoanOffer Offer)> RequireEligibleAsync(CancellationToken ct)
    {
        var result = await GetEligibilityAsync(ct);
        if (!result.Eligible || result.Offer is null)
            throw new DomainException("credit.not_eligible", "O empréstimo ainda não está disponível para você.");
        return (eligibility.Profile, result.Offer);
    }

    private async Task<LoanOffer> OfferAsync(CreditProfile p, CancellationToken ct)
    {
        var band = p.Policy.BandFor(p.Score ?? 0)!;
        var income = p.Customer.MonthlyIncome ?? 0;
        var outstanding = await db.LoanInstallments
            .Where(i => i.Status != InstallmentStatus.Paid && db.Loans.Any(l => l.Id == i.LoanId && l.UserId == p.Customer.UserId))
            .SumAsync(i => (decimal?)i.Principal, ct) ?? 0;
        var maxAmount = Math.Max(0, Money.Round(income * band.PreApprovedIncomeMultiplier - outstanding));
        var maxInstallment = Math.Max(0, Money.Round(income * p.Policy.MaxIncomeCommitment - p.MonthlyCommitment));
        return new LoanOffer(maxAmount, band.MonthlyRate, p.Policy.MinInstallments, p.Policy.MaxInstallments, p.Policy.MinLoanAmount, maxInstallment);
    }

    private LoanQuote Quote(CreditProfile p, LoanOffer offer, decimal amount, int installments)
    {
        if (installments < offer.MinInstallments || installments > offer.MaxInstallments)
            throw new DomainException("credit.installments", $"Escolha entre {offer.MinInstallments} e {offer.MaxInstallments} parcelas.");
        if (amount < offer.MinAmount)
            throw new DomainException("credit.amount_below_min", $"O valor mínimo é {Formatting.Brl(offer.MinAmount)}.");
        if (amount > offer.MaxAmount)
            throw new DomainException("credit.amount_above_offer", $"Seu limite pré-aprovado é de {Formatting.Brl(offer.MaxAmount)}.");
        var quote = LoanMath.Quote(amount, installments, offer.MonthlyRate, Today(), Iof);
        if (quote.InstallmentAmount > offer.MaxInstallmentAmount)
            throw new DomainException("credit.income_commitment",
                $"A parcela de {Formatting.Brl(quote.InstallmentAmount)} passa de {Formatting.Percent(p.Policy.MaxIncomeCommitment)} da sua renda. Aumente o número de parcelas ou reduza o valor.");
        return quote;
    }

    private static LoanResponse ToResponse(Loan l, DateOnly today, CreditPolicy policy) => new(
        l.Id, l.Amount, l.NetAmount, l.Iof, l.MonthlyRate, l.CetMonthly, l.CetYearly, l.InstallmentCount, l.InstallmentAmount, l.Status,
        l.OutstandingPrincipal, l.ContractedAt,
        l.Installments.OrderBy(i => i.Number).Select(i => new InstallmentResponse(
            i.Number, i.DueDate, i.Amount, i.Interest, i.Principal, i.BalanceAfter, i.Status, i.PaidAmount, i.PaidAt,
            i.Status == InstallmentStatus.Paid ? null : i.ChargeOn(today, l.MonthlyRate, policy.LateFeeRate, policy.DailyLateInterestRate).Total))
            .ToList());

    private DateOnly Today() => DateOnly.FromDateTime(BankTime.ToLocal(clock.GetUtcNow().UtcDateTime));
}
