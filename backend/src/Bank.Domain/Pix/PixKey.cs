using System.Net.Mail;
using System.Text.Json.Serialization;
using Bank.Domain.Common;
using Bank.Domain.Customers;

namespace Bank.Domain.Pix;

[JsonConverter(typeof(JsonStringEnumConverter<PixKeyType>))]
public enum PixKeyType
{
    Cpf,
    Email,
    Phone,
    Random,
}

/// <summary>An alias (CPF, e-mail, phone or random) that points to an account.</summary>
public class PixKey : Entity
{
    /// <summary>Individuals can register up to 5 keys per account (Central Bank rule).</summary>
    public const int MaxKeysPerAccount = 5;

    private PixKey() { }

    public Guid AccountId { get; private set; }
    public PixKeyType Type { get; private set; }
    /// <summary>Normalized: CPF digits, lower-case e-mail, E.164 phone, lower-case UUID.</summary>
    public string Value { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    public static PixKey Register(Guid accountId, PixKeyType type, string? value, string ownerCpf, int keysAlreadyRegistered, DateTime now)
    {
        if (keysAlreadyRegistered >= MaxKeysPerAccount)
            throw new DomainException("pix.key_limit", $"Você já tem {MaxKeysPerAccount} chaves PIX, o máximo permitido.");

        var normalized = type == PixKeyType.Random
            ? Guid.NewGuid().ToString()
            : PixKeys.Normalize(type, value) ?? throw new DomainException("pix.key_invalid", "Chave PIX inválida para o tipo escolhido.");
        if (type == PixKeyType.Cpf && normalized != ownerCpf)
            throw new DomainException("pix.key_not_owner", "A chave CPF precisa ser o CPF do titular da conta.");

        return new PixKey { AccountId = accountId, Type = type, Value = normalized, CreatedAt = now };
    }
}

public static class PixKeys
{
    public static string? Normalize(PixKeyType type, string? value) => type switch
    {
        PixKeyType.Cpf => Cpf.IsValid(value) ? Cpf.Normalize(value!) : null,
        PixKeyType.Email => IsEmail(value) ? value!.Trim().ToLowerInvariant() : null,
        PixKeyType.Phone => Phone.TryNormalize(value),
        PixKeyType.Random => Guid.TryParse(value, out var g) ? g.ToString() : null,
        _ => null,
    };

    /// <summary>
    /// Infers the key type from what the payer typed. Phones must start with "+55" (as in the Central
    /// Bank directory), otherwise an 11-digit mobile number would be indistinguishable from a CPF.
    /// </summary>
    public static (PixKeyType Type, string Value)? Detect(string? input)
    {
        var raw = input?.Trim() ?? "";
        if (raw.Contains('@')) return Pair(PixKeyType.Email, raw);
        if (raw.StartsWith('+')) return Pair(PixKeyType.Phone, raw);
        if (Guid.TryParse(raw, out _)) return Pair(PixKeyType.Random, raw);
        return Pair(PixKeyType.Cpf, raw);

        static (PixKeyType, string)? Pair(PixKeyType type, string raw) =>
            Normalize(type, raw) is { } value ? (type, value) : null;
    }

    /// <summary>Partially hidden key for receipts and confirmation screens.</summary>
    public static string Mask(PixKeyType type, string value) => type switch
    {
        PixKeyType.Cpf => Cpf.Mask(value),
        PixKeyType.Email => value[..Math.Min(2, value.IndexOf('@'))] + "***" + value[value.IndexOf('@')..],
        PixKeyType.Phone => value[..5] + "*****" + value[^4..],
        _ => value,
    };

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 77 && MailAddress.TryCreate(value.Trim(), out var a) && a.Address == value.Trim();
}
