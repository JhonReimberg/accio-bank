using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Accounts;

public enum AccountType
{
    /// <summary>A customer's checking account. Can never go below zero.</summary>
    Customer,
    /// <summary>The bank's own books (cash, revenue...). Counterpart of customer movements; may be negative.</summary>
    System,
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountStatus>))]
public enum AccountStatus
{
    /// <summary>Opened, waiting for KYC approval: no money movements yet.</summary>
    PendingReview,
    Active,
    /// <summary>Frozen by the bank: can receive money, cannot send.</summary>
    Blocked,
    Closed,
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountTier>))]
public enum AccountTier
{
    Basic,
    Plus,
    Premium,
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
    public AccountStatus Status { get; private set; }
    public AccountTier Tier { get; private set; }
    public string? StatusReason { get; private set; }
    /// <summary>Total ledger balance, including blocked funds.</summary>
    public decimal Balance { get; private set; }
    /// <summary>Part of the balance that cannot be spent (judicial holds, guarantees, fraud review).</summary>
    public decimal BlockedBalance { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ActivatedAt { get; private set; }

    public decimal AvailableBalance => Balance - BlockedBalance;

    public static Account OpenCustomer(Guid userId, DateTime now) =>
        new() { Type = AccountType.Customer, UserId = userId, Status = AccountStatus.PendingReview, Tier = AccountTier.Basic, CreatedAt = now };

    public static Account OpenSystem(Guid id, long number, string name, DateTime now) =>
        new() { Id = id, Type = AccountType.System, Number = number, SystemName = name, Status = AccountStatus.Active, CreatedAt = now };

    /// <summary>Tier by declared monthly income; it sets limits and which products are offered.</summary>
    public static AccountTier TierForIncome(decimal monthlyIncome) => monthlyIncome switch
    {
        < 5_000m => AccountTier.Basic,
        < 15_000m => AccountTier.Plus,
        _ => AccountTier.Premium,
    };

    // Only the ledger moves money, so every balance change has matching entries.
    internal void ApplyCredit(decimal amount)
    {
        if (Type == AccountType.Customer && Status is not (AccountStatus.Active or AccountStatus.Blocked))
            throw new DomainException("account.not_active", "A conta de destino não pode receber valores.");
        Balance += amount;
    }

    internal void ApplyDebit(decimal amount)
    {
        if (Type == AccountType.Customer)
        {
            EnsureCanSend();
            if (AvailableBalance < amount)
                throw new DomainException("account.insufficient_funds", "Saldo insuficiente.");
        }
        Balance -= amount;
    }

    public void EnsureCanSend()
    {
        switch (Status)
        {
            case AccountStatus.PendingReview:
                throw new DomainException("account.pending_review", "Sua conta ainda está em análise.");
            case AccountStatus.Blocked:
                throw new DomainException("account.blocked", "Sua conta está bloqueada. Fale com o atendimento.");
            case AccountStatus.Closed:
                throw new DomainException("account.closed", "Esta conta está encerrada.");
        }
    }

    public void Activate(AccountTier tier, DateTime now)
    {
        if (Status != AccountStatus.PendingReview)
            throw new DomainException("account.invalid_status", "A conta não está aguardando ativação.");
        Status = AccountStatus.Active;
        Tier = tier;
        ActivatedAt = now;
    }

    public void ChangeTier(AccountTier tier) => Tier = tier;

    public void Block(string reason)
    {
        if (Status != AccountStatus.Active)
            throw new DomainException("account.invalid_status", "Só contas ativas podem ser bloqueadas.");
        Status = AccountStatus.Blocked;
        StatusReason = reason;
        Raise(new AccountStatusChanged(Id, UserId, Status, reason));
    }

    public void Unblock()
    {
        if (Status != AccountStatus.Blocked)
            throw new DomainException("account.invalid_status", "A conta não está bloqueada.");
        Status = AccountStatus.Active;
        StatusReason = null;
        Raise(new AccountStatusChanged(Id, UserId, Status, null));
    }

    public void Close(string reason)
    {
        if (Status is AccountStatus.Closed)
            throw new DomainException("account.invalid_status", "A conta já está encerrada.");
        if (Balance != 0 || BlockedBalance != 0)
            throw new DomainException("account.has_balance", "Só é possível encerrar a conta com saldo zerado.");
        Status = AccountStatus.Closed;
        StatusReason = reason;
        Raise(new AccountStatusChanged(Id, UserId, Status, reason));
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

public sealed record AccountStatusChanged(Guid AccountId, Guid? UserId, AccountStatus Status, string? Reason) : IDomainEvent;
