using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Ledger;

namespace Bank.Application.Ledger;

/// <summary>The one way money moves: lock the accounts, post a balanced transaction, commit.</summary>
public class LedgerPosting(IBankDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Runs <paramref name="build"/> with the locked accounts inside a database transaction and persists the result.
    /// Any exception (e.g. insufficient funds) rolls everything back.
    /// </summary>
    public async Task<LedgerTransaction> ExecuteAsync(
        IReadOnlyCollection<Guid> accountIds,
        Func<IReadOnlyDictionary<Guid, Account>, DateTime, LedgerTransaction> build,
        CancellationToken ct,
        Action<LedgerTransaction>? beforeSave = null)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var accounts = (await db.LockAccountsAsync(accountIds, ct)).ToDictionary(a => a.Id);
        var transaction = build(accounts, clock.GetUtcNow().UtcDateTime);
        db.LedgerTransactions.Add(transaction);
        beforeSave?.Invoke(transaction);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return transaction;
    }
}
