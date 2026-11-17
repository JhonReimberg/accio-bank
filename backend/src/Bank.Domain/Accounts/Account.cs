using Bank.Domain.Common;

namespace Bank.Domain.Accounts;

public enum AccountType
{
    /// <summary>A customer's checking account. Can never go below zero.</summary>
    Customer,
    /// <summary>The bank's own books (cash, revenue...). Counterpart of customer movements; may be negative.</summary>
    System,
}

/// <summary>
/// Ledger account. Balance is materialized for fast reads but is always equal to
/// sum(credits) - sum(debits) of its ledger entries (checked by the reconciliation query).
/// </summary>
public class Account : Entity
{
    private Account() { }

    public AccountType Type { get; private set; }
    public Guid? UserId { get; private set; }
    /// <summary>Assigned by a database sequence for customer accounts.</summary>
    public long Number { get; private set; }
    public string? SystemName { get; private set; }
    /// <summary>Total ledger balance, including blocked funds.</summary>
    public decimal Balance { get; private set; }
    /// <summary>Part of the balance that cannot be spent (judicial holds, guarantees, fraud review).</summary>
    public decimal BlockedBalance { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public decimal AvailableBalance => Balance - BlockedBalance;

    public static Account OpenCustomer(Guid userId, DateTime now) =>
        new() { Type = AccountType.Customer, UserId = userId, CreatedAt = now };

    public static Account OpenSystem(Guid id, long number, string name, DateTime now) =>
        new() { Id = id, Type = AccountType.System, Number = number, SystemName = name, CreatedAt = now };

    // Only the ledger moves money, so every balance change has matching entries.
    internal void ApplyCredit(decimal amount) => Balance += amount;

    internal void ApplyDebit(decimal amount)
    {
        if (Type == AccountType.Customer && AvailableBalance < amount)
            throw new DomainException("account.insufficient_funds", "Saldo insuficiente.");
        Balance -= amount;
    }

    public BalanceHold PlaceHold(decimal amount, string reason, DateTime now)
    {
        Money.EnsureValidAmount(amount);
        if (AvailableBalance < amount)
            throw new DomainException("account.insufficient_funds", "Saldo disponível insuficiente para o bloqueio.");
        BlockedBalance += amount;
        return BalanceHold.Create(Id, amount, reason, now);
    }

    public void ReleaseHold(BalanceHold hold, DateTime now)
    {
        if (hold.AccountId != Id)
            throw new DomainException("hold.wrong_account", "O bloqueio não pertence a esta conta.");
        hold.Release(now);
        BlockedBalance -= hold.Amount;
    }
}
