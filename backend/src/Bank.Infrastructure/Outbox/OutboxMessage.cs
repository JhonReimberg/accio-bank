using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Outbox;

/// <summary>A domain event saved in the same transaction as the change that raised it, delivered later.</summary>
public class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Type { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTime OccurredAt { get; init; }
    public DateTime? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    public void MarkProcessed(DateTime now) => ProcessedAt = now;

    public void MarkFailed(string error)
    {
        ProcessedAt = null;
        Attempts++;
        Error = error.Length > 2000 ? error[..2000] : error;
    }
}

internal class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.Property(m => m.Type).HasMaxLength(300);
        b.Property(m => m.Payload).HasColumnType("jsonb");
        // Partial index: the processor only ever scans pending messages.
        b.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
    }
}
