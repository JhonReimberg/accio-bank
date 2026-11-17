using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Auditing;
using Bank.Domain.Common;
using Bank.Domain.Ledger;
using Bank.Domain.Notifications;
using Bank.Domain.Users;
using Bank.Infrastructure.Idempotency;
using Bank.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Bank.Infrastructure.Persistence;

public class BankDbContext(DbContextOptions<BankDbContext> options, TimeProvider clock) : DbContext(options), IBankDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BalanceHold> BalanceHolds => Set<BalanceHold>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("account_numbers").StartsAt(10_000_001);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        MoveDomainEventsToOutbox();
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

    /// <summary>Events become rows of this same transaction: they are published if and only if the change commits.</summary>
    private void MoveDomainEventsToOutbox()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entity in ChangeTracker.Entries<Entity>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList())
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxProcessor.TypeName(domainEvent.GetType()),
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OutboxProcessor.Json),
                    OccurredAt = now,
                });
            }
            entity.ClearDomainEvents();
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
