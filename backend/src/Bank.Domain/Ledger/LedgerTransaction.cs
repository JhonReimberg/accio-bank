using System.Text.Json.Serialization;
using Bank.Domain.Accounts;
using Bank.Domain.Common;

namespace Bank.Domain.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<TransactionKind>))]
public enum TransactionKind
{
    Deposit,
    InternalTransfer,
    Pix,
    PixRefund,
}

[JsonConverter(typeof(JsonStringEnumConverter<EntryDirection>))]
public enum EntryDirection
{
    /// <summary>Takes money out of the account.</summary>
    Debit,
    /// <summary>Puts money into the account.</summary>
    Credit,
}

/// <summary>One leg of a ledger transaction.</summary>
public sealed record Posting(Account Account, EntryDirection Direction, decimal Amount, string Description);

/// <summary>
/// A balanced double-entry transaction: the sum of debits always equals the sum of credits,
/// so money is never created or destroyed, only moved between accounts.
/// </summary>
public class LedgerTransaction : Entity
{
    private readonly List<LedgerEntry> _entries = [];

    private LedgerTransaction() { }

    public TransactionKind Kind { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public static LedgerTransaction Post(TransactionKind kind, DateTime now, params IReadOnlyList<Posting> postings)
    {
        if (postings.Count < 2)
            throw new InvalidOperationException("A ledger transaction needs at least two postings.");
        foreach (var p in postings) Money.EnsureValidAmount(p.Amount);
        var debits = postings.Where(p => p.Direction == EntryDirection.Debit).Sum(p => p.Amount);
        var credits = postings.Where(p => p.Direction == EntryDirection.Credit).Sum(p => p.Amount);
        if (debits != credits)
            throw new InvalidOperationException($"Unbalanced ledger transaction: debits {debits} != credits {credits}.");

        var tx = new LedgerTransaction { Kind = kind, CreatedAt = now };
        // Debits first: if one fails (insufficient funds) nothing has been credited yet.
        foreach (var p in postings.OrderBy(p => p.Direction == EntryDirection.Credit))
        {
            if (p.Direction == EntryDirection.Debit) p.Account.ApplyDebit(p.Amount);
            else p.Account.ApplyCredit(p.Amount);
            tx._entries.Add(LedgerEntry.Create(tx.Id, p.Account.Id, p.Direction, p.Amount, p.Account.Balance, p.Description, now));
        }
        return tx;
    }

    /// <summary>The common two-leg case: move an amount from one account to another.</summary>
    public static LedgerTransaction Transfer(
        TransactionKind kind, Account from, Account to, decimal amount, string debitDescription, string creditDescription, DateTime now)
    {
        if (from.Id == to.Id)
            throw new DomainException("transfer.same_account", "Não é possível transferir para a própria conta.");
        return Post(kind, now,
            new Posting(from, EntryDirection.Debit, amount, debitDescription),
            new Posting(to, EntryDirection.Credit, amount, creditDescription));
    }
}

public class LedgerEntry : Entity
{
    private LedgerEntry() { }

    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryDirection Direction { get; private set; }
    /// <summary>Always positive; the direction says which way it moved.</summary>
    public decimal Amount { get; private set; }
    /// <summary>Account balance right after this entry (audit trail and statement running balance).</summary>
    public decimal BalanceAfter { get; private set; }
    public string Description { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    /// <summary>Signed amount from the account holder's point of view.</summary>
    public decimal SignedAmount => Direction == EntryDirection.Credit ? Amount : -Amount;

    internal static LedgerEntry Create(Guid transactionId, Guid accountId, EntryDirection direction, decimal amount, decimal balanceAfter, string description, DateTime now) => new()
    {
        TransactionId = transactionId,
        AccountId = accountId,
        Direction = direction,
        Amount = amount,
        BalanceAfter = balanceAfter,
        Description = description,
        CreatedAt = now,
    };
}
