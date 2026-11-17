using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Accounts;

[JsonConverter(typeof(JsonStringEnumConverter<TransactionType>))]
public enum TransactionType
{
    Deposit,
    TransferIn,
    TransferOut,
}

/// <summary>Statement line. Amount is signed: positive = money in, negative = money out.</summary>
public class Transaction : Entity
{
    private Transaction() { }

    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = "";
    public Guid? CounterpartyAccountId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Transaction Record(Guid accountId, TransactionType type, decimal amount, string description, Guid? counterparty, DateTime now) => new()
    {
        AccountId = accountId,
        Type = type,
        Amount = amount,
        Description = description,
        CounterpartyAccountId = counterparty,
        CreatedAt = now,
    };
}
