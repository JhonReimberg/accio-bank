using Bank.Domain.Common;

namespace Bank.Application.Common;

/// <summary>pt-BR formatting that does not depend on the server culture (containers run invariant).</summary>
public static class Formatting
{
    public static string Brl(decimal amount) => Money.Brl(amount);

    public static string Percent(decimal ratio) => Money.Percent(ratio);
}
