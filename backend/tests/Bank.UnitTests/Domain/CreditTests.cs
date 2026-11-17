using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Credit;
using Bank.Domain.Ledger;
using static Bank.UnitTests.Domain.AccountTests;

namespace Bank.UnitTests.Domain;

public class LoanMathTests
{
    private static readonly DateOnly Contract = new(2026, 1, 31);

    [Fact]
    public void Price_installment_matches_the_textbook_formula()
    {
        // 10,000 at 2% a.m. in 12x → 945.60 (PMT = PV·i / (1 − (1+i)^−n)).
        Assert.Equal(945.60m, LoanMath.Installment(10_000, 0.02m, 12));
        Assert.Equal(1_000m, LoanMath.Installment(12_000, 0, 12));
    }

    [Fact]
    public void Amortization_table_has_falling_interest_and_repays_exactly_the_principal()
    {
        var rows = LoanMath.Schedule(10_000, 0.02m, 12, new DateOnly(2026, 2, 28));

        Assert.Equal((200.00m, 745.60m, 9_254.40m), (rows[0].Interest, rows[0].Principal, rows[0].Balance));
        Assert.True(rows.Zip(rows.Skip(1)).All(p => p.Second.Interest < p.First.Interest));
        Assert.Equal(10_000m, rows.Sum(r => r.Principal));
        Assert.Equal(0m, rows[^1].Balance);
        // Every installment but the last is the same value; the last absorbs rounding cents.
        Assert.All(rows.SkipLast(1), r => Assert.Equal(945.60m, r.Payment));
        Assert.InRange(rows[^1].Payment, 945.50m, 945.70m);
    }

    [Fact]
    public void Due_dates_keep_the_day_of_month_when_possible()
    {
        var rows = LoanMath.Schedule(1_000, 0.03m, 3, Contract);
        Assert.Equal([new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)], rows.Select(r => r.DueDate));
    }

    [Fact]
    public void Quote_includes_iof_and_a_cet_above_the_nominal_rate()
    {
        var quote = LoanMath.Quote(5_000, 12, 0.03m, Contract, new IofRates());

        Assert.True(quote.Iof > 0);
        Assert.Equal(quote.RequestedAmount - quote.Iof, quote.NetAmount);
        Assert.True(quote.CetMonthly > 0.03m, "IOF makes the effective cost higher than the contract rate");
        Assert.Equal(quote.Schedule.Sum(r => r.Payment) - 5_000, quote.TotalInterest);
        // CET definition: the net amount received equals the payments discounted at the CET.
        var pv = quote.Schedule.Select((r, k) => r.Payment / LoanMath.Pow(1 + quote.CetMonthly, k + 1)).Sum();
        Assert.InRange(pv, quote.NetAmount - 0.05m, quote.NetAmount + 0.05m);
    }

    [Fact]
    public void Iof_is_flat_plus_daily_capped_at_365_days()
    {
        var shortLoan = LoanMath.Schedule(1_000, 0, 1, Contract.AddDays(30));
        Assert.Equal(Money.Round(1_000 * 0.0038m + 1_000 * 0.000082m * 30), LoanMath.Iof(shortLoan, Contract, new IofRates()));
        var longLoan = LoanMath.Schedule(1_000, 0, 1, Contract.AddDays(500));
        Assert.Equal(Money.Round(1_000 * 0.0038m + 1_000 * 0.000082m * 365), LoanMath.Iof(longLoan, Contract, new IofRates()));
    }
}

public class LoanInstallmentTests
{
    private static Loan NewLoan()
    {
        var quote = LoanMath.Quote(10_000, 12, 0.02m, new DateOnly(2026, 1, 10), new IofRates());
        return Loan.Contract(Guid.NewGuid(), Guid.NewGuid(), quote, 700, Guid.NewGuid(), DateTime.UtcNow);
    }

    [Fact]
    public void Late_payment_adds_2_percent_fine_and_daily_default_interest()
    {
        var first = NewLoan().Installment(1); // due 2026-02-10, 945.60
        var charge = first.ChargeOn(new DateOnly(2026, 2, 20), 0.02m, 0.02m, 0.00033m);
        Assert.Equal((18.91m, 3.12m, 0m), (charge.LateFee, charge.LateInterest, charge.Discount));
        Assert.Equal(945.60m + 18.91m + 3.12m, charge.Total);
    }

    [Fact]
    public void Paying_on_the_due_date_costs_exactly_the_installment()
    {
        var first = NewLoan().Installment(1);
        Assert.Equal(945.60m, first.ChargeOn(first.DueDate, 0.02m, 0.02m, 0.00033m).Total);
    }

    [Fact]
    public void Early_payment_discounts_interest_but_never_the_principal()
    {
        var loan = NewLoan();
        var first = loan.Installment(1);
        var month = first.ChargeOn(first.DueDate.AddDays(-30), 0.02m, 0.02m, 0.00033m);
        Assert.Equal(927.06m, month.Total); // 945.60 / 1.02
        // Paying the last installment almost a year early: the discount stops at its interest.
        var last = loan.Installment(12);
        var early = last.ChargeOn(new DateOnly(2026, 1, 11), 0.02m, 0.02m, 0.00033m);
        Assert.Equal(last.Interest, early.Discount);
        Assert.Equal(last.Principal, early.Total);
    }

    [Fact]
    public void Overdue_marking_raises_events_and_a_fully_paid_loan_is_closed()
    {
        var loan = NewLoan();
        var overdue = loan.MarkOverdue(new DateOnly(2026, 3, 11));
        Assert.Equal([1, 2], overdue.Select(i => i.Number));
        Assert.Equal(2, loan.DomainEvents.OfType<LoanInstallmentOverdue>().Count());

        foreach (var i in loan.Installments)
            loan.RegisterPayment(i, i.ChargeOn(i.DueDate, 0.02m, 0.02m, 0.00033m), Guid.NewGuid(), i.DueDate, DateTime.UtcNow);
        Assert.Equal(LoanStatus.PaidOff, loan.Status);
        Assert.Equal(0m, loan.OutstandingPrincipal);
        Assert.True(loan.Installment(1).WasLate);
        Assert.False(loan.Installment(3).WasLate);
    }
}

public class ScoreTests
{
    private static ScoreInputs Good() => new(true, 365, 10_000, 20_000, 10, 0, 0, 0);

    [Fact]
    public void Score_is_explainable_and_bounded()
    {
        var (score, factors) = ScoreCalculator.Calculate(Good());
        Assert.Equal(factors.Sum(f => f.Points), score);
        Assert.Equal(300 + 150 + 150 + 100 + 150, score);
    }

    [Fact]
    public void Late_and_overdue_installments_and_overdraft_usage_lower_the_score()
    {
        var (good, _) = ScoreCalculator.Calculate(Good());
        var (bad, factors) = ScoreCalculator.Calculate(Good() with { InstallmentsPaidLate = 2, InstallmentsOverdueNow = 1, OverdraftUsageRatio = 0.5m });
        Assert.Equal(good - 120 - 200 - 50, bad);
        Assert.Contains(factors, f => f.Code == "overdue");
    }

    [Fact]
    public void Without_approved_kyc_the_score_is_zero() =>
        Assert.Equal(0, ScoreCalculator.Calculate(Good() with { KycApproved = false }).Score);
}

public class CreditPolicyTests
{
    [Theory]
    [InlineData(399, null)]
    [InlineData(400, 400)]
    [InlineData(799, 600)]
    [InlineData(1000, 800)]
    public void Band_is_the_highest_one_the_score_reaches(int score, int? band) =>
        Assert.Equal(band, new CreditPolicy().BandFor(score)?.MinScore);

    [Fact]
    public void Admin_cannot_set_a_fine_above_2_percent_or_bands_where_better_scores_pay_more()
    {
        var policy = new CreditPolicy();
        CreditPolicyValues Values(decimal fee, params ScoreBand[] bands) =>
            new(60, 1500, 400, 0.3m, 3, 36, 300, fee, 0.00033m, 0.08m, 0.1m, bands);
        var ok = new ScoreBand { MinScore = 400, MonthlyRate = 0.05m, PreApprovedIncomeMultiplier = 1, OverdraftLimit = 500 };

        Assert.Throws<DomainException>(() => policy.Update(Values(0.03m, ok), Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => policy.Update(Values(0.02m, ok,
            new ScoreBand { MinScore = 700, MonthlyRate = 0.06m, PreApprovedIncomeMultiplier = 2, OverdraftLimit = 900 }), Guid.NewGuid(), DateTime.UtcNow));
        policy.Update(Values(0.02m, ok), Guid.NewGuid(), DateTime.UtcNow);
        Assert.Single(policy.Bands);
    }

    [Fact]
    public void Overdraft_daily_rate_compounds_to_the_monthly_rate() =>
        Assert.InRange(LoanMath.Pow(1 + new CreditPolicy().OverdraftDailyRate, 30) - 1, 0.0799m, 0.0801m);
}

public class OverdraftTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Overdraft_lets_the_balance_go_negative_up_to_the_limit()
    {
        var account = Customer(100);
        account.SetOverdraftLimit(500);
        LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 600, "x", "y", Now);
        Assert.Equal((-500m, 500m, 0m), (account.Balance, account.OverdraftUsed, account.SpendingPower));
        Assert.Throws<DomainException>(() => LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 0.01m, "x", "y", Now));
        Assert.Throws<DomainException>(() => account.SetOverdraftLimit(499));
    }

    [Fact]
    public void Interest_charge_never_goes_beyond_the_limit()
    {
        var account = Customer();
        account.SetOverdraftLimit(100);
        LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 99, "x", "y", Now);
        var revenue = Account.OpenSystem(Guid.NewGuid(), 3, "Receita", Now);
        var tx = LedgerTransaction.Charge(TransactionKind.OverdraftInterest, account, revenue, 5, "Juros", Now);
        Assert.Equal(1m, tx!.Entries[0].Amount);
        Assert.Equal(-100m, account.Balance);
        Assert.Null(LedgerTransaction.Charge(TransactionKind.OverdraftInterest, account, revenue, 5, "Juros", Now));
    }
}
