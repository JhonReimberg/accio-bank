using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Ledger;

namespace Bank.Application.Ledger;

/// <summary>The one way money moves: lock the accounts, post a balanced transaction, commit.</summary>
public class LedgerPosting(IBankDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Runs <paramref name="build"/> with the locked accounts inside a database transaction and persists the
    /// result. Checks that depend on other data (e.g. PIX limits) belong inside <paramref name="build"/>: the
    /// row locks make them safe against concurrent requests. Any exception rolls everything back.
    /// </summary>
    public async Task<LedgerTransaction> ExecuteAsync(
        IReadOnlyCollection<Guid> accountIds,
        Func<IReadOnlyDictionary<Guid, Account>, DateTime, Task<LedgerTransaction>> build,
        CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var accounts = (await db.LockAccountsAsync(accountIds, ct)).ToDictionary(a => a.Id);
        var transaction = await build(accounts, clock.GetUtcNow().UtcDateTime);
        db.LedgerTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return transaction;
    }

    /// <summary>Like <see cref="ExecuteAsync(IReadOnlyCollection{Guid}, Func{IReadOnlyDictionary{Guid, Account}, DateTime, Task{LedgerTransaction}}, CancellationToken)"/>, but <paramref name="build"/> may decide there is nothing to post (returns null).</summary>
    public async Task<LedgerTransaction?> ExecuteIfNeededAsync(
        IReadOnlyCollection<Guid> accountIds,
        Func<IReadOnlyDictionary<Guid, Account>, DateTime, LedgerTransaction?> build,
        CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var accounts = (await db.LockAccountsAsync(accountIds, ct)).ToDictionary(a => a.Id);
        var transaction = build(accounts, clock.GetUtcNow().UtcDateTime);
        if (transaction is null) return null; // disposing the transaction releases the locks
        db.LedgerTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return transaction;
    }

    public Task<LedgerTransaction> ExecuteAsync(
        IReadOnlyCollection<Guid> accountIds,
        Func<IReadOnlyDictionary<Guid, Account>, DateTime, LedgerTransaction> build,
        CancellationToken ct,
        Action<LedgerTransaction>? beforeSave = null) =>
        ExecuteAsync(accountIds, (accounts, now) =>
        {
            var transaction = build(accounts, now);
            beforeSave?.Invoke(transaction);
            return Task.FromResult(transaction);
        }, ct);
}
