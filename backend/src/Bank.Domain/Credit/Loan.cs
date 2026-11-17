using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Credit;

[JsonConverter(typeof(JsonStringEnumConverter<LoanStatus>))]
public enum LoanStatus
{
    Active,
    PaidOff,
}

[JsonConverter(typeof(JsonStringEnumConverter<InstallmentStatus>))]
public enum InstallmentStatus
{
    Pending,
    Overdue,
    Paid,
}

/// <summary>What has to be paid for an installment today, broken down.</summary>
public sealed record InstallmentCharge(decimal Scheduled, decimal Principal, decimal Interest, decimal LateFee, decimal LateInterest, decimal Discount)
{
    public decimal Total => Scheduled + LateFee + LateInterest - Discount;
    /// <summary>Everything above the principal is the bank's revenue.</summary>
    public decimal Revenue => Total - Principal;
}

/// <summary>A personal loan (Tabela Price) and its installment schedule.</summary>
public class Loan : Entity
{
    private readonly List<LoanInstallment> _installments = [];

    private Loan() { }

    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal MonthlyRate { get; private set; }
    public int InstallmentCount { get; private set; }
    public decimal InstallmentAmount { get; private set; }
    public decimal Iof { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal CetMonthly { get; private set; }
    public decimal CetYearly { get; private set; }
    public int ScoreAtContract { get; private set; }
    public LoanStatus Status { get; private set; }
    public Guid DisbursementTransactionId { get; private set; }
    public DateTime ContractedAt { get; private set; }
    public IReadOnlyList<LoanInstallment> Installments => _installments;

    public decimal OutstandingPrincipal => _installments.Where(i => i.Status != InstallmentStatus.Paid).Sum(i => i.Principal);

    public static Loan Contract(Guid accountId, Guid userId, LoanQuote quote, int score, Guid disbursementTransactionId, DateTime now)
    {
        var loan = new Loan
        {
            AccountId = accountId,
            UserId = userId,
            Amount = quote.RequestedAmount,
            MonthlyRate = quote.MonthlyRate,
            InstallmentCount = quote.Installments,
            InstallmentAmount = quote.InstallmentAmount,
            Iof = quote.Iof,
            NetAmount = quote.NetAmount,
            CetMonthly = quote.CetMonthly,
            CetYearly = quote.CetYearly,
            ScoreAtContract = score,
            Status = LoanStatus.Active,
            DisbursementTransactionId = disbursementTransactionId,
            ContractedAt = now,
        };
        foreach (var row in quote.Schedule)
            loan._installments.Add(LoanInstallment.Create(loan.Id, row));
        return loan;
    }

    public LoanInstallment Installment(int number) =>
        _installments.SingleOrDefault(i => i.Number == number) ?? throw new DomainException("loan.installment_not_found", "Parcela não encontrada.");

    /// <summary>Marks unpaid installments past their due date as overdue. Returns the ones that just became overdue.</summary>
    public IReadOnlyList<LoanInstallment> MarkOverdue(DateOnly today)
    {
        var late = _installments.Where(i => i.Status == InstallmentStatus.Pending && i.DueDate < today).ToList();
        foreach (var installment in late)
        {
            installment.MarkOverdue();
            Raise(new LoanInstallmentOverdue(Id, UserId, installment.Number, installment.Amount, installment.DueDate));
        }
        return late;
    }

    public void RegisterPayment(LoanInstallment installment, InstallmentCharge charge, Guid transactionId, DateOnly today, DateTime now)
    {
        if (installment.LoanId != Id) throw new InvalidOperationException("Installment of another loan.");
        installment.Pay(charge, transactionId, today, now);
        if (_installments.All(i => i.Status == InstallmentStatus.Paid))
        {
            Status = LoanStatus.PaidOff;
            Raise(new LoanPaidOff(Id, UserId));
        }
    }
}

public class LoanInstallment : Entity
{
    private LoanInstallment() { }

    public Guid LoanId { get; private set; }
    public int Number { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal Interest { get; private set; }
    public decimal Principal { get; private set; }
    /// <summary>Contract balance after this installment (amortization table).</summary>
    public decimal BalanceAfter { get; private set; }
    public InstallmentStatus Status { get; private set; }
    public bool WasLate { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public decimal? PaidAmount { get; private set; }
    public decimal LateFee { get; private set; }
    public decimal LateInterest { get; private set; }
    public decimal Discount { get; private set; }
    public Guid? PaymentTransactionId { get; private set; }

    internal static LoanInstallment Create(Guid loanId, AmortizationRow row) => new()
    {
        LoanId = loanId,
        Number = row.Number,
        DueDate = row.DueDate,
        Amount = row.Payment,
        Interest = row.Interest,
        Principal = row.Principal,
        BalanceAfter = row.Balance,
        Status = InstallmentStatus.Pending,
    };

    /// <summary>
    /// Amount due on <paramref name="today"/>:
    /// late → scheduled + 2% fine + daily default interest for each day late;
    /// early → scheduled minus the interest saved by paying before the due date (present value at the
    /// contract rate), capped at this installment's interest so the principal is always repaid in full.
    /// </summary>
    public InstallmentCharge ChargeOn(DateOnly today, decimal monthlyRate, decimal lateFeeRate, decimal dailyLateRate)
    {
        if (Status == InstallmentStatus.Paid)
            throw new DomainException("loan.installment_paid", "Esta parcela já foi paga.");
        var daysLate = today.DayNumber - DueDate.DayNumber;
        if (daysLate > 0)
            return new InstallmentCharge(Amount, Principal, Interest, Money.Round(Amount * lateFeeRate), Money.Round(Amount * dailyLateRate * daysLate), 0);
        var discount = Math.Min(Interest, Amount - LoanMath.PresentValue(Amount, monthlyRate, -daysLate));
        return new InstallmentCharge(Amount, Principal, Interest, 0, 0, Math.Max(0, discount));
    }

    internal void MarkOverdue()
    {
        Status = InstallmentStatus.Overdue;
        WasLate = true;
    }

    internal void Pay(InstallmentCharge charge, Guid transactionId, DateOnly today, DateTime now)
    {
        if (Status == InstallmentStatus.Paid) throw new DomainException("loan.installment_paid", "Esta parcela já foi paga.");
        if (today > DueDate) WasLate = true;
        Status = InstallmentStatus.Paid;
        PaidAt = now;
        PaidAmount = charge.Total;
        LateFee = charge.LateFee;
        LateInterest = charge.LateInterest;
        Discount = charge.Discount;
        PaymentTransactionId = transactionId;
    }
}

public sealed record LoanInstallmentOverdue(Guid LoanId, Guid UserId, int Number, decimal Amount, DateOnly DueDate) : IDomainEvent;

public sealed record LoanPaidOff(Guid LoanId, Guid UserId) : IDomainEvent;
