using Bank.Domain.Common;

namespace Bank.Domain.Accounts;

public enum HoldStatus
{
    Active,
    Released,
}

/// <summary>Funds set aside on an account: they stay in the balance but are not available to spend.</summary>
public class BalanceHold : Entity
{
    private BalanceHold() { }

    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = "";
    public HoldStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }

    internal static BalanceHold Create(Guid accountId, decimal amount, string reason, DateTime now) => new()
    {
        AccountId = accountId,
        Amount = amount,
        Reason = reason,
        Status = HoldStatus.Active,
        CreatedAt = now,
    };

    internal void Release(DateTime now)
    {
        if (Status != HoldStatus.Active)
            throw new DomainException("hold.not_active", "Este bloqueio já foi liberado.");
        Status = HoldStatus.Released;
        ReleasedAt = now;
    }
}
