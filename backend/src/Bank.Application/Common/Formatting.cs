using System.Globalization;

namespace Bank.Application.Common;

public static class Formatting
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string Brl(decimal amount) => amount.ToString("C", PtBr);
}
