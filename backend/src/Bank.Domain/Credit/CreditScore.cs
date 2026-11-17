using Bank.Domain.Common;

namespace Bank.Domain.Credit;

/// <summary>What the score is computed from (gathered by the application from the ledger and loans).</summary>
public sealed record ScoreInputs(
    bool KycApproved,
    int AccountAgeDays,
    decimal Credits90Days,
    decimal MonthlyIncome,
    int InstallmentsPaidOnTime,
    int InstallmentsPaidLate,
    int InstallmentsOverdueNow,
    decimal OverdraftUsageRatio);

public sealed record ScoreFactor(string Code, string Description, int Points);

/// <summary>
/// Internal score from 0 to 1000. Explainable on purpose: every point comes from a named factor,
/// stored with the score so analysts (and the customer) can see why.
/// </summary>
public static class ScoreCalculator
{
    public const int Base = 300;

    public static (int Score, IReadOnlyList<ScoreFactor> Factors) Calculate(ScoreInputs i)
    {
        if (!i.KycApproved) return (0, [new ScoreFactor("kyc", "Cadastro ainda não aprovado", 0)]);

        var factors = new List<ScoreFactor>
        {
            new("base", "Pontuação inicial", Base),
            new("account_age", $"Tempo de conta: {i.AccountAgeDays} dias", (int)Math.Round(Math.Min(i.AccountAgeDays, 365) / 365m * 150)),
            new("movement", $"Movimentação nos últimos 90 dias: {Money.Brl(i.Credits90Days)}", (int)Math.Round(Math.Min(i.Credits90Days / 10_000m, 1) * 150)),
            new("income", $"Renda declarada: {Money.Brl(i.MonthlyIncome)}", (int)Math.Round(Math.Min(i.MonthlyIncome / 20_000m, 1) * 100)),
            new("paid_on_time", $"{i.InstallmentsPaidOnTime} parcela(s) paga(s) em dia", Math.Min(i.InstallmentsPaidOnTime * 20, 150)),
        };
        if (i.InstallmentsPaidLate > 0)
            factors.Add(new("paid_late", $"{i.InstallmentsPaidLate} parcela(s) paga(s) com atraso", -Math.Min(i.InstallmentsPaidLate * 60, 300)));
        if (i.InstallmentsOverdueNow > 0)
            factors.Add(new("overdue", $"{i.InstallmentsOverdueNow} parcela(s) em atraso agora", -200));
        if (i.OverdraftUsageRatio > 0)
            factors.Add(new("overdraft_usage", $"Uso do cheque especial: {Money.Percent(i.OverdraftUsageRatio)}", -(int)Math.Round(Math.Min(i.OverdraftUsageRatio, 1) * 100)));

        return (Math.Clamp(factors.Sum(f => f.Points), 0, 1000), factors);
    }
}

/// <summary>A score calculation kept as history, with the factors that produced it.</summary>
public class CreditScore : Entity
{
    private CreditScore() { }

    public Guid UserId { get; private set; }
    public int Score { get; private set; }
    /// <summary>JSON array of <see cref="ScoreFactor"/>.</summary>
    public string Factors { get; private set; } = "[]";
    public DateTime CalculatedAt { get; private set; }

    public static CreditScore Record(Guid userId, int score, string factorsJson, DateTime now) =>
        new() { UserId = userId, Score = score, Factors = factorsJson, CalculatedAt = now };
}
