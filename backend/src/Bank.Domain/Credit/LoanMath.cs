using Bank.Domain.Common;

namespace Bank.Domain.Credit;

public sealed record AmortizationRow(int Number, DateOnly DueDate, decimal Payment, decimal Interest, decimal Principal, decimal Balance);

public sealed record LoanQuote(
    decimal RequestedAmount,
    int Installments,
    decimal MonthlyRate,
    decimal InstallmentAmount,
    decimal Iof,
    decimal NetAmount,
    decimal TotalInterest,
    decimal TotalPaid,
    decimal CetMonthly,
    decimal CetYearly,
    IReadOnlyList<AmortizationRow> Schedule);

/// <summary>IOF for individuals, simplified: a flat rate plus a daily rate on each installment's principal (capped at 365 days).</summary>
public sealed record IofRates(decimal Flat = 0.0038m, decimal Daily = 0.000082m);

/// <summary>Tabela Price (French amortization): equal installments, decreasing interest, increasing principal.</summary>
public static class LoanMath
{
    /// <summary>PMT = PV × i / (1 − (1 + i)^−n).</summary>
    public static decimal Installment(decimal principal, decimal monthlyRate, int installments)
    {
        if (installments < 1) throw new ArgumentOutOfRangeException(nameof(installments));
        if (monthlyRate == 0) return Money.Round(principal / installments);
        var factor = Pow(1 + monthlyRate, -installments);
        return Money.Round(principal * monthlyRate / (1 - factor));
    }

    /// <summary>
    /// Full amortization table. Interest of each month is rounded to cents; the last installment absorbs
    /// rounding residue so the principal paid adds up exactly to the amount borrowed.
    /// </summary>
    public static IReadOnlyList<AmortizationRow> Schedule(decimal principal, decimal monthlyRate, int installments, DateOnly firstDueDate)
    {
        var payment = Installment(principal, monthlyRate, installments);
        var rows = new List<AmortizationRow>(installments);
        var balance = principal;
        for (var k = 1; k <= installments; k++)
        {
            var interest = Money.Round(balance * monthlyRate);
            var amortization = k == installments ? balance : payment - interest;
            balance -= amortization;
            rows.Add(new AmortizationRow(k, DueDate(firstDueDate, k), amortization + interest, interest, amortization, balance));
        }
        return rows;
    }

    /// <summary>Same day of month as the first due date when possible (31 → 30/28 → 31).</summary>
    public static DateOnly DueDate(DateOnly first, int number)
    {
        var month = first.AddMonths(number - 1);
        return new DateOnly(month.Year, month.Month, Math.Min(first.Day, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    public static decimal Iof(IReadOnlyList<AmortizationRow> schedule, DateOnly contractDate, IofRates rates)
    {
        var principal = schedule.Sum(r => r.Principal);
        var daily = schedule.Sum(r => r.Principal * rates.Daily * Math.Min(365, r.DueDate.DayNumber - contractDate.DayNumber));
        return Money.Round(principal * rates.Flat + daily);
    }

    public static LoanQuote Quote(decimal amount, int installments, decimal monthlyRate, DateOnly contractDate, IofRates iofRates)
    {
        Money.EnsureValidAmount(amount);
        var schedule = Schedule(amount, monthlyRate, installments, contractDate.AddMonths(1));
        var iof = Iof(schedule, contractDate, iofRates);
        var net = amount - iof;
        var totalPaid = schedule.Sum(r => r.Payment);
        // CET: the monthly rate that makes the cash actually received equal to the payments made.
        var cet = InternalRate(net, schedule.Select(r => r.Payment).ToList());
        return new LoanQuote(amount, installments, monthlyRate, schedule[0].Payment, iof, net, totalPaid - amount, totalPaid,
            Math.Round(cet, 6), Math.Round(Pow(1 + cet, 12) - 1, 6), schedule);
    }

    /// <summary>Present value of a future payment, discounting the contract rate for the days left (early payment).</summary>
    public static decimal PresentValue(decimal amount, decimal monthlyRate, int daysEarly) =>
        daysEarly <= 0 ? amount : Money.Round(amount / Pow(1 + monthlyRate, daysEarly / 30m));

    /// <summary>Monthly rate r with Σ payment_k / (1 + r)^k = present value, by bisection (robust, no derivatives).</summary>
    public static decimal InternalRate(decimal presentValue, IReadOnlyList<decimal> payments)
    {
        decimal low = 0, high = 1;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var mid = (low + high) / 2;
            decimal pv = 0;
            for (var k = 0; k < payments.Count; k++) pv += payments[k] / Pow(1 + mid, k + 1);
            if (pv > presentValue) low = mid; else high = mid;
        }
        return (low + high) / 2;
    }

    /// <summary>decimal power with a fractional/negative exponent (via double: precision is plenty for rates).</summary>
    public static decimal Pow(decimal value, decimal exponent) => (decimal)Math.Pow((double)value, (double)exponent);
}
