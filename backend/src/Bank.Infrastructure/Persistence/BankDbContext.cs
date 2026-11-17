using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Ledger;
using Bank.Domain.Users;
using Bank.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Bank.Infrastructure.Persistence;

public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options), IBankDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BalanceHold> BalanceHolds => Set<BalanceHold>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("account_numbers").StartsAt(10_000_001);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests raced past the application-level duplicate check.
            throw new ConflictException("Registro duplicado.");
        }
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => Database.BeginTransactionAsync(ct);

    public async Task<IReadOnlyList<Account>> LockAccountsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks only last inside a transaction.");

        // A tracked instance would be returned as-is (stale balance) instead of the freshly locked row.
        foreach (var entry in ChangeTracker.Entries<Account>().Where(e => ids.Contains(e.Entity.Id)).ToList())
            entry.State = EntityState.Detached;

        var idArray = ids.Distinct().ToArray();
        return await Accounts
            .FromSql($"SELECT * FROM accounts WHERE id = ANY({idArray}) ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
    }
}
