using Bank.Domain.Accounts;
using Bank.Domain.Ledger;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bank.Application.Abstractions;

public interface IBankDbContext
{
    DbSet<User> Users { get; }
    DbSet<Account> Accounts { get; }
    DbSet<BalanceHold> BalanceHolds { get; }
    DbSet<LedgerTransaction> LedgerTransactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);

    /// <summary>
    /// Loads the accounts with a row lock (SELECT ... FOR UPDATE), always in id order so that two
    /// operations touching the same pair of accounts cannot deadlock. Must run inside a transaction.
    /// </summary>
    Task<IReadOnlyList<Account>> LockAccountsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
