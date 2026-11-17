using Bank.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Idempotency;

public class IdempotencyRecord
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

internal class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasKey(r => new { r.UserId, r.Key });
        b.Property(r => r.Key).HasMaxLength(100);
        b.Property(r => r.RequestHash).HasMaxLength(64);
    }
}

public enum IdempotencyBegin
{
    /// <summary>First time this key is seen: run the operation.</summary>
    Started,
    /// <summary>Same key and same request already completed: replay the stored response.</summary>
    Completed,
    /// <summary>Same key is being processed by another request right now.</summary>
    InProgress,
    /// <summary>Same key was used with a different request body.</summary>
    Mismatch,
}

/// <summary>
/// Stores idempotency keys with plain SQL (never through the change tracker), so recording or
/// abandoning a key cannot accidentally save half-done entities of the operation itself.
/// </summary>
public class IdempotencyStore(BankDbContext db, TimeProvider clock)
{
    public async Task<(IdempotencyBegin State, IdempotencyRecord? Existing)> BeginAsync(
        Guid userId, string key, string requestHash, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // The primary key (user_id, key) makes the insert the arbiter between concurrent duplicates.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO idempotency_records (user_id, key, request_hash, created_at)
            VALUES ({userId}, {key}, {requestHash}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 1) return (IdempotencyBegin.Started, null);

        var existing = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.UserId == userId && r.Key == key, ct);
        if (existing.RequestHash != requestHash) return (IdempotencyBegin.Mismatch, existing);
        // shortcut: a crash mid-request leaves the key "in progress" forever; expire stale keys if that matters.
        return existing.CompletedAt is null ? (IdempotencyBegin.InProgress, existing) : (IdempotencyBegin.Completed, existing);
    }

    public Task CompleteAsync(Guid userId, string key, int statusCode, string? body, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.IdempotencyRecords.Where(r => r.UserId == userId && r.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, body)
                .SetProperty(r => r.CompletedAt, now), ct);
    }

    /// <summary>The operation failed, so nothing happened: free the key for a retry.</summary>
    public Task AbandonAsync(Guid userId, string key) =>
        db.IdempotencyRecords.Where(r => r.UserId == userId && r.Key == key).ExecuteDeleteAsync(CancellationToken.None);
}
