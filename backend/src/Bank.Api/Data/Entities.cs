using System.Text.Json.Serialization;

namespace Bank.Api.Data;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Account Account { get; set; } = null!;
}

public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    // Filled by a Postgres sequence, so numbers never collide.
    public long Number { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter<TransactionType>))]
public enum TransactionType
{
    Deposit,
    TransferIn,
    TransferOut,
}

// Ledger entry. Amount is signed: positive = money in, negative = money out.
public class Transaction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public Guid? CounterpartyAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
