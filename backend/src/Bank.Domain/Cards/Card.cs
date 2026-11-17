using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Cards;

[JsonConverter(typeof(JsonStringEnumConverter<CardFunction>))]
public enum CardFunction { Debit, Credit }

[JsonConverter(typeof(JsonStringEnumConverter<CardForm>))]
public enum CardForm { Physical, Virtual, VirtualTemporary }

[JsonConverter(typeof(JsonStringEnumConverter<CardStatus>))]
public enum CardStatus { Active, Blocked, Cancelled }

/// <summary>Card numbers that pass the Luhn check (fictitious BIN, never a real issuer).</summary>
public static class Luhn
{
    public const string Bin = "559899";

    public static bool IsValid(string number) =>
        number.Length is >= 12 and <= 19 && number.All(char.IsAsciiDigit) && CheckDigit(number[..^1]) == number[^1] - '0';

    public static string Generate(Random random)
    {
        var body = Bin + string.Concat(Enumerable.Range(0, 9).Select(_ => random.Next(10)));
        return body + CheckDigit(body);
    }

    /// <summary>Doubles every second digit from the right (of the payload), sums digits, completes to a multiple of 10.</summary>
    private static int CheckDigit(string payload)
    {
        var sum = 0;
        for (var i = 0; i < payload.Length; i++)
        {
            var d = payload[payload.Length - 1 - i] - '0';
            if (i % 2 == 0) d = d * 2 > 9 ? d * 2 - 9 : d * 2;
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }
}

public class Card : Entity
{
    private Card() { }

    public Guid AccountId { get; private set; }
    public CardFunction Function { get; private set; }
    public CardForm Form { get; private set; }
    public string Number { get; private set; } = "";
    public string HolderName { get; private set; } = "";
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }
    public CardStatus Status { get; private set; }
    public bool OnlinePurchasesEnabled { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string Last4 => Number[^4..];
    public string Masked => $"•••• {Last4}";

    public static Card Issue(Guid accountId, CardFunction function, CardForm form, string holderName, DateTime now, Random random)
    {
        if (function == CardFunction.Debit && form == CardForm.VirtualTemporary)
            throw new DomainException("card.invalid_form", "Cartão temporário é só de crédito.");
        var expiry = now.AddYears(form == CardForm.Physical ? 5 : 1);
        return new Card
        {
            AccountId = accountId,
            Function = function,
            Form = form,
            Number = Luhn.Generate(random),
            HolderName = holderName.ToUpperInvariant(),
            ExpiryMonth = expiry.Month,
            ExpiryYear = expiry.Year,
            Status = CardStatus.Active,
            // Virtual cards exist for online shopping; physical ones start with it off (safer default).
            OnlinePurchasesEnabled = form != CardForm.Physical,
            CreatedAt = now,
        };
    }

    public void EnsureCanPurchase(bool online, DateTime now)
    {
        if (Status == CardStatus.Blocked) throw new DomainException("card.blocked", "Cartão bloqueado. Desbloqueie pelo app.");
        if (Status == CardStatus.Cancelled) throw new DomainException("card.cancelled", "Este cartão foi cancelado.");
        if (new DateTime(ExpiryYear, ExpiryMonth, 1).AddMonths(1) <= now) throw new DomainException("card.expired", "Cartão vencido.");
        if (online && !OnlinePurchasesEnabled) throw new DomainException("card.online_disabled", "Compras online estão desativadas neste cartão.");
    }

    /// <summary>A temporary virtual card dies after its first approved purchase.</summary>
    public void PurchaseApproved()
    {
        if (Form == CardForm.VirtualTemporary) Status = CardStatus.Cancelled;
    }

    public void Block() => Status = Status == CardStatus.Cancelled ? throw new DomainException("card.cancelled", "Este cartão foi cancelado.") : CardStatus.Blocked;

    public void Unblock()
    {
        if (Status != CardStatus.Blocked) throw new DomainException("card.not_blocked", "O cartão não está bloqueado.");
        Status = CardStatus.Active;
    }

    public void Cancel() => Status = CardStatus.Cancelled;

    public void SetOnlinePurchases(bool enabled) => OnlinePurchasesEnabled = enabled;
}
