using Bank.Domain.Common;

namespace Bank.Domain.Accounts;

public class Account : Entity
{
    private Account() { }

    public Guid UserId { get; private set; }
    /// <summary>Assigned by a database sequence.</summary>
    public long Number { get; private set; }
    public decimal Balance { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Account Open(Guid userId, DateTime now) => new() { UserId = userId, CreatedAt = now };

    public void Credit(decimal amount)
    {
        Money.EnsureValidAmount(amount);
        Balance += amount;
    }

    public void Debit(decimal amount)
    {
        Money.EnsureValidAmount(amount);
        if (Balance < amount)
            throw new DomainException("account.insufficient_funds", "Saldo insuficiente.");
        Balance -= amount;
    }
}
