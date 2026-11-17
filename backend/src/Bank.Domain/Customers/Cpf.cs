using Bank.Domain.Common;

namespace Bank.Domain.Customers;

/// <summary>Brazilian individual taxpayer id, validated by its two check digits. Stored as 11 digits.</summary>
public static class Cpf
{
    public static bool IsValid(string? value)
    {
        var digits = Digits(value);
        if (digits.Length != 11 || digits.Distinct().Count() == 1) return false;
        return CheckDigit(digits, 9) == digits[9] - '0' && CheckDigit(digits, 10) == digits[10] - '0';
    }

    /// <summary>Returns the 11 digits, or throws when the CPF is invalid.</summary>
    public static string Normalize(string value)
    {
        if (!IsValid(value)) throw new DomainException("cpf.invalid", "CPF inválido.");
        return Digits(value);
    }

    public static string Format(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    /// <summary>LGPD-friendly display: ***.456.789-**.</summary>
    public static string Mask(string cpf) => $"***.{cpf[3..6]}.{cpf[6..9]}-**";

    /// <summary>Generates a valid, random CPF. For seed and test data only.</summary>
    public static string Generate(Random random)
    {
        string digits;
        do
        {
            digits = string.Concat(Enumerable.Range(0, 9).Select(_ => random.Next(10)));
        } while (digits.Distinct().Count() == 1);
        digits += CheckDigit(digits, 9);
        return digits + CheckDigit(digits, 10);
    }

    // Weighted sum mod 11 over the first n digits, weights n+1 down to 2.
    private static int CheckDigit(string digits, int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++) sum += (digits[i] - '0') * (n + 1 - i);
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
