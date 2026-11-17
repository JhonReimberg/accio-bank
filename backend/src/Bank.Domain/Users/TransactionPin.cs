using Bank.Domain.Common;

namespace Bank.Domain.Users;

/// <summary>Rules for the 4-digit transaction PIN (separate from the login password).</summary>
public static class TransactionPin
{
    public static void EnsureStrong(string pin)
    {
        if (pin.Length != 4 || !pin.All(char.IsAsciiDigit))
            throw new DomainException("pin.format", "A senha transacional deve ter exatamente 4 dígitos.");
        if (pin.Distinct().Count() == 1)
            throw new DomainException("pin.weak", "Não use todos os dígitos iguais.");
        if (IsSequence(pin, +1) || IsSequence(pin, -1))
            throw new DomainException("pin.weak", "Não use sequências como 1234 ou 4321.");
    }

    private static bool IsSequence(string pin, int step)
    {
        for (var i = 1; i < pin.Length; i++)
            if (pin[i] - pin[i - 1] != step) return false;
        return true;
    }
}
