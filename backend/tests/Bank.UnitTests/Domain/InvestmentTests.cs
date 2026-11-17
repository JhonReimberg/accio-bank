using Bank.Domain.Common;
using Bank.Domain.Investments;

namespace Bank.UnitTests.Domain;

public class InvestmentTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 0.225)]
    [InlineData(180, 0.225)]
    [InlineData(181, 0.20)]
    [InlineData(360, 0.20)]
    [InlineData(361, 0.175)]
    [InlineData(720, 0.175)]
    [InlineData(721, 0.15)]
    public void Income_tax_is_regressive(int days, decimal rate) => Assert.Equal(rate, InvestmentTax.IncomeTaxRate(days));

    [Theory]
    [InlineData(0, 0.96)]
    [InlineData(1, 0.96)]
    [InlineData(15, 0.50)]
    [InlineData(29, 0.03)]
    [InlineData(30, 0)]
    public void Iof_applies_only_in_the_first_30_days(int days, decimal rate) => Assert.Equal(rate, InvestmentTax.IofRate(days));

    [Fact]
    public void Daily_rate_compounds_to_the_annual_cdi_over_252_business_days()
    {
        var daily = InvestmentTax.DailyRate(0.1065m, 1m);
        Assert.InRange(Bank.Domain.Credit.LoanMath.Pow(1 + daily, 252) - 1, 0.10649m, 0.10651m);
        Assert.Equal(daily * 1.1m, InvestmentTax.DailyRate(0.1065m, 1.1m));
    }

    private static Investment WithYield(decimal amount, decimal yield, DateTime appliedAt)
    {
        var investment = Investment.Open(Guid.NewGuid(), InvestmentType.Cdb, "CDB", null, 1m, appliedAt);
        investment.Apply(amount, appliedAt);
        investment.AccrueDailyYield(yield / amount); // one accrual equal to the desired income
        return investment;
    }

    [Fact]
    public void Redemption_after_one_year_pays_17_5_percent_on_income_and_no_iof()
    {
        var investment = WithYield(1_000, 100, Start);
        var r = investment.Redeem(1_100, Start.AddDays(400));
        Assert.Equal((1_000m, 100m, 0m, 17.50m, 1_082.50m), (r.Principal, r.Income, r.Iof, r.IncomeTax, r.Net));
        Assert.Equal(0m, investment.Value);
    }

    [Fact]
    public void Redemption_in_the_first_days_pays_iof_before_income_tax()
    {
        var investment = WithYield(1_000, 10, Start);
        var r = investment.Redeem(1_010, Start.AddDays(15));
        // IOF 50% of 10 = 5; IR 22.5% of (10 - 5) = 1.13 (banker's rounding of 1.125 → 1.12)
        Assert.Equal((5m, 1.12m), (r.Iof, r.IncomeTax));
        Assert.Equal(1_003.88m, r.Net);
    }

    [Fact]
    public void Partial_redemptions_are_fifo_and_keep_the_principal_proportional()
    {
        var investment = Investment.Open(Guid.NewGuid(), InvestmentType.Caixinha, "Viagem", 5_000, 1m, Start);
        investment.Apply(1_000, Start);
        investment.Apply(1_000, Start.AddDays(200));
        var r = investment.Redeem(500, Start.AddDays(400));
        Assert.Equal(500m, r.Principal); // from the oldest lot first
        Assert.Equal(1_500m, investment.Value);
        Assert.Equal(2, investment.Lots.Count);
    }

    [Fact]
    public void Blocked_collateral_cannot_be_redeemed_and_round_up_is_only_for_caixinhas()
    {
        var investment = WithYield(1_000, 0.01m, Start);
        investment.BlockAsCollateral(800);
        Assert.Equal("investment.insufficient", Assert.Throws<DomainException>(() => investment.Redeem(300, Start.AddDays(40))).Code);
        investment.Redeem(200, Start.AddDays(40));
        Assert.Throws<DomainException>(() => investment.SetRoundUp(true));
    }
}
