using System.Globalization;

namespace Bank.Domain.Common;

public static class Money
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>"R$ 1.234,56" regardless of the server culture (containers run invariant).</summary>
    public static string Brl(decimal amount) => amount.ToString("C", PtBr);

    /// <summary>0.3 → "30%", 0.045 → "4,5%".</summary>
    public static string Percent(decimal ratio) => (ratio * 100).ToString("0.##", PtBr) + "%";

    /// <summary>Banker's rounding (half to even) to cents, used for every computed amount.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    /// <summary>Amounts informed by users must be positive and already in cents.</summary>
    public static void EnsureValidAmount(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("amount.not_positive", "O valor deve ser maior que zero.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("amount.precision", "Use no máximo 2 casas decimais.");
    }
}
