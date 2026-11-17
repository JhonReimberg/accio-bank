using Bank.Domain.Common;

namespace Bank.Domain.Credit;

/// <summary>A score range: from <see cref="MinScore"/> up to the next band.</summary>
public class ScoreBand
{
    public int MinScore { get; set; }
    /// <summary>Personal loan rate, per month (e.g. 0.045 = 4.5% a.m.).</summary>
    public decimal MonthlyRate { get; set; }
    /// <summary>Pre-approved loan amount as a multiple of declared monthly income.</summary>
    public decimal PreApprovedIncomeMultiplier { get; set; }
    public decimal OverdraftLimit { get; set; }
}

/// <summary>
/// Credit parameters tuned by the Admin in the back-office (not hard-coded): eligibility thresholds,
/// rate bands by score, late payment charges, overdraft rate and CDI. A single row.
/// </summary>
public class CreditPolicy
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public int MinAccountAgeDays { get; private set; } = 60;
    public decimal MinMovement30Days { get; private set; } = 1_500m;
    public int MinScore { get; private set; } = 400;
    /// <summary>Sum of loan installments must stay under this share of declared income.</summary>
    public decimal MaxIncomeCommitment { get; private set; } = 0.30m;
    public int MinInstallments { get; private set; } = 3;
    public int MaxInstallments { get; private set; } = 36;
    public decimal MinLoanAmount { get; private set; } = 300m;
    /// <summary>One-off fine on a late installment (2%).</summary>
    public decimal LateFeeRate { get; private set; } = 0.02m;
    /// <summary>Default interest per day late (0.033% ≈ 1% a.m.).</summary>
    public decimal DailyLateInterestRate { get; private set; } = 0.00033m;
    public decimal OverdraftMonthlyRate { get; private set; } = 0.08m;
    /// <summary>Reference rate for investments (phase 3), kept here so the Admin edits all rates in one place.</summary>
    public decimal CdiAnnualRate { get; private set; } = 0.1065m;
    /// <summary>Rate for loans backed by an investment (lower: the bank holds collateral).</summary>
    public decimal GuaranteedMonthlyRate { get; private set; } = 0.019m;
    public List<ScoreBand> Bands { get; private set; } =
    [
        new() { MinScore = 400, MonthlyRate = 0.069m, PreApprovedIncomeMultiplier = 1m, OverdraftLimit = 500m },
        new() { MinScore = 600, MonthlyRate = 0.045m, PreApprovedIncomeMultiplier = 3m, OverdraftLimit = 2_000m },
        new() { MinScore = 800, MonthlyRate = 0.029m, PreApprovedIncomeMultiplier = 5m, OverdraftLimit = 5_000m },
    ];
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    /// <summary>The band a score falls in, or null when it is below every band (no credit).</summary>
    public ScoreBand? BandFor(int score) => Bands.Where(b => b.MinScore <= score).MaxBy(b => b.MinScore);

    /// <summary>Overdraft daily rate equivalent to the monthly rate (compound, 30-day month).</summary>
    public decimal OverdraftDailyRate => LoanMath.Pow(1 + OverdraftMonthlyRate, 1m / 30) - 1;

    public void Update(CreditPolicyValues v, Guid updatedBy, DateTime now)
    {
        Ensure(v.MinAccountAgeDays is >= 0 and <= 3650, "Tempo mínimo de conta inválido.");
        Ensure(v.MinMovement30Days >= 0, "Movimentação mínima inválida.");
        Ensure(v.MinScore is >= 0 and <= 1000, "Score mínimo deve estar entre 0 e 1000.");
        Ensure(v.MaxIncomeCommitment is > 0 and <= 1, "Comprometimento de renda deve estar entre 0 e 100%.");
        Ensure(v.MinInstallments >= 1 && v.MaxInstallments >= v.MinInstallments && v.MaxInstallments <= 120, "Faixa de parcelas inválida.");
        Ensure(v.MinLoanAmount > 0, "Valor mínimo de empréstimo inválido.");
        Ensure(v.LateFeeRate is >= 0 and <= 0.02m, "A multa por atraso é limitada a 2% (CDC).");
        Ensure(v.DailyLateInterestRate is >= 0 and <= 0.01m, "Juros de mora diários inválidos.");
        Ensure(v.OverdraftMonthlyRate is >= 0 and <= 0.08m, "Juros do cheque especial limitados a 8% a.m. (regra do BC).");
        Ensure(v.CdiAnnualRate is >= 0 and <= 1, "CDI inválido.");
        Ensure(v.GuaranteedMonthlyRate is > 0 and < 0.2m, "Taxa do crédito com garantia inválida.");
        Ensure(v.Bands.Count > 0, "Informe ao menos uma faixa de score.");
        Ensure(v.Bands.Select(b => b.MinScore).Distinct().Count() == v.Bands.Count, "Faixas com score mínimo repetido.");
        foreach (var b in v.Bands)
        {
            Ensure(b.MinScore is >= 0 and <= 1000, "Score da faixa deve estar entre 0 e 1000.");
            Ensure(b.MonthlyRate is > 0 and < 0.2m, "Taxa mensal da faixa inválida.");
            Ensure(b.PreApprovedIncomeMultiplier is >= 0 and <= 20, "Multiplicador de renda inválido.");
            Ensure(b.OverdraftLimit >= 0, "Limite de cheque especial inválido.");
        }
        // Better scores never pay more: rates must fall as the minimum score rises.
        var ordered = v.Bands.OrderBy(b => b.MinScore).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Ensure(ordered[i].MonthlyRate <= ordered[i - 1].MonthlyRate, "Faixas com score maior devem ter taxa menor ou igual.");

        (MinAccountAgeDays, MinMovement30Days, MinScore, MaxIncomeCommitment, MinInstallments, MaxInstallments, MinLoanAmount) =
            (v.MinAccountAgeDays, v.MinMovement30Days, v.MinScore, v.MaxIncomeCommitment, v.MinInstallments, v.MaxInstallments, v.MinLoanAmount);
        (LateFeeRate, DailyLateInterestRate, OverdraftMonthlyRate, CdiAnnualRate, GuaranteedMonthlyRate) =
            (v.LateFeeRate, v.DailyLateInterestRate, v.OverdraftMonthlyRate, v.CdiAnnualRate, v.GuaranteedMonthlyRate);
        Bands = ordered;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new DomainException("credit_policy.invalid", message);
    }
}

public sealed record CreditPolicyValues(
    int MinAccountAgeDays, decimal MinMovement30Days, int MinScore, decimal MaxIncomeCommitment,
    int MinInstallments, int MaxInstallments, decimal MinLoanAmount,
    decimal LateFeeRate, decimal DailyLateInterestRate, decimal OverdraftMonthlyRate, decimal CdiAnnualRate,
    IReadOnlyList<ScoreBand> Bands,
    decimal GuaranteedMonthlyRate = 0.019m);
