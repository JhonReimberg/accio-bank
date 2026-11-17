using System.Text.Json.Serialization;
using Bank.Domain.Common;
using Bank.Domain.Credit;

namespace Bank.Domain.Investments;

[JsonConverter(typeof(JsonStringEnumConverter<InvestmentType>))]
public enum InvestmentType
{
    /// <summary>CDB with daily liquidity.</summary>
    Cdb,
    /// <summary>Savings goal ("caixinha"): a CDB with a name and a target.</summary>
    Caixinha,
}

/// <summary>Regressive taxes on fixed income (Brazil).</summary>
public static class InvestmentTax
{
    /// <summary>IR on income by holding period: 22.5% up to 180 days, 20% up to 360, 17.5% up to 720, 15% after.</summary>
    public static decimal IncomeTaxRate(int days) => days switch
    {
        <= 180 => 0.225m,
        <= 360 => 0.20m,
        <= 720 => 0.175m,
        _ => 0.15m,
    };

    /// <summary>IOF on income for redemptions in the first 30 days: 96% on day 1 falling to 0% on day 30.</summary>
    public static decimal IofRate(int days) => days >= 30 ? 0 : Table[Math.Max(days, 1) - 1];

    // Official regressive IOF table (days 1..29).
    private static readonly decimal[] Table =
    [
        0.96m, 0.93m, 0.90m, 0.86m, 0.83m, 0.80m, 0.76m, 0.73m, 0.70m, 0.66m, 0.63m, 0.60m, 0.56m, 0.53m, 0.50m,
        0.46m, 0.43m, 0.40m, 0.36m, 0.33m, 0.30m, 0.26m, 0.23m, 0.20m, 0.16m, 0.13m, 0.10m, 0.06m, 0.03m,
    ];

    /// <summary>Daily rate for a % of an annual CDI, on 252 business days.</summary>
    public static decimal DailyRate(decimal cdiAnnual, decimal cdiPercent) =>
        (LoanMath.Pow(1 + cdiAnnual, 1m / 252) - 1) * cdiPercent;
}

public sealed record RedemptionBreakdown(decimal Gross, decimal Principal, decimal Income, decimal Iof, decimal IncomeTax)
{
    public decimal Net => Gross - Iof - IncomeTax;
}

/// <summary>A fixed-income position. Each application is a lot, so taxes follow the age of each deposit.</summary>
public class Investment : Entity
{
    private readonly List<InvestmentLot> _lots = [];

    private Investment() { }

    public Guid AccountId { get; private set; }
    public InvestmentType Type { get; private set; }
    public string Name { get; private set; } = "";
    public decimal? Goal { get; private set; }
    /// <summary>Share of CDI paid (1.0 = 100% do CDI).</summary>
    public decimal CdiPercent { get; private set; }
    /// <summary>Part of the value pledged as loan collateral; cannot be redeemed.</summary>
    public decimal BlockedAmount { get; private set; }
    public bool RoundUpCardPurchases { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<InvestmentLot> Lots => _lots;

    public decimal Principal => _lots.Sum(l => l.Principal);
    public decimal Value => _lots.Sum(l => l.Value);
    public decimal Income => Value - Principal;
    public decimal AvailableToRedeem => Math.Max(0, Value - BlockedAmount);

    public static Investment Open(Guid accountId, InvestmentType type, string name, decimal? goal, decimal cdiPercent, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("investment.name", "Dê um nome ao investimento.");
        if (goal is not null) Money.EnsureValidAmount(goal.Value);
        return new Investment { AccountId = accountId, Type = type, Name = name.Trim(), Goal = goal, CdiPercent = cdiPercent, CreatedAt = now };
    }

    public void Apply(decimal amount, DateTime now)
    {
        Money.EnsureValidAmount(amount);
        _lots.Add(InvestmentLot.Create(Id, amount, now));
    }

    public void SetRoundUp(bool enabled)
    {
        if (enabled && Type != InvestmentType.Caixinha)
            throw new DomainException("investment.roundup", "O arredondamento de compras só vale para caixinhas.");
        RoundUpCardPurchases = enabled;
    }

    /// <summary>One business day of yield on every lot, rounded to cents (returns the total accrued).</summary>
    public decimal AccrueDailyYield(decimal dailyCdiRate)
    {
        var total = 0m;
        foreach (var lot in _lots) total += lot.Accrue(dailyCdiRate * CdiPercent);
        return total;
    }

    /// <summary>
    /// Redeems <paramref name="gross"/> FIFO across lots. IOF (first 30 days) and IR (regressive) are computed
    /// per lot on its income share, so an old deposit pays 15% while a recent one pays 22.5%.
    /// </summary>
    public RedemptionBreakdown Redeem(decimal gross, DateTime now)
    {
        Money.EnsureValidAmount(gross);
        if (gross > AvailableToRedeem)
            throw new DomainException("investment.insufficient", $"Disponível para resgate: {Money.Brl(AvailableToRedeem)}.");

        decimal principal = 0, income = 0, iof = 0, tax = 0, left = gross;
        foreach (var lot in _lots.Where(l => l.Value > 0).OrderBy(l => l.AppliedAt).ToList())
        {
            if (left == 0) break;
            var take = Math.Min(left, lot.Value);
            var share = take / lot.Value;
            var lotPrincipal = Money.Round(lot.Principal * share);
            var lotIncome = take - lotPrincipal;
            var days = (int)(now - lot.AppliedAt).TotalDays;
            var lotIof = Money.Round(Math.Max(0, lotIncome) * InvestmentTax.IofRate(days));
            var lotTax = Money.Round(Math.Max(0, lotIncome - lotIof) * InvestmentTax.IncomeTaxRate(days));
            lot.Withdraw(take, lotPrincipal);
            (principal, income, iof, tax, left) = (principal + lotPrincipal, income + lotIncome, iof + lotIof, tax + lotTax, left - take);
        }
        _lots.RemoveAll(l => l.Value == 0 && l.Principal == 0);
        return new RedemptionBreakdown(gross, principal, income, iof, tax);
    }

    public void BlockAsCollateral(decimal amount)
    {
        Money.EnsureValidAmount(amount);
        if (amount > AvailableToRedeem)
            throw new DomainException("investment.collateral", $"Valor disponível como garantia: {Money.Brl(AvailableToRedeem)}.");
        BlockedAmount += amount;
    }

    public void ReleaseCollateral(decimal amount) => BlockedAmount = Math.Max(0, BlockedAmount - amount);
}

public class InvestmentLot : Entity
{
    private InvestmentLot() { }

    public Guid InvestmentId { get; private set; }
    public decimal Principal { get; private set; }
    public decimal Value { get; private set; }
    public DateTime AppliedAt { get; private set; }

    internal static InvestmentLot Create(Guid investmentId, decimal amount, DateTime now) =>
        new() { InvestmentId = investmentId, Principal = amount, Value = amount, AppliedAt = now };

    internal decimal Accrue(decimal rate)
    {
        var gain = Money.Round(Value * rate);
        Value += gain;
        return gain;
    }

    internal void Withdraw(decimal value, decimal principal)
    {
        Value -= value;
        Principal -= principal;
    }
}
