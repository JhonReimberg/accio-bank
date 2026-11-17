using Bank.Domain.Auditing;
using Bank.Domain.Notifications;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(a => a.Action).HasMaxLength(100);
        b.Property(a => a.EntityType).HasMaxLength(100);
        b.Property(a => a.EntityId).HasMaxLength(100);
        b.Property(a => a.Before).HasColumnType("jsonb");
        b.Property(a => a.After).HasColumnType("jsonb");
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => new { a.ActorUserId, a.CreatedAt });
        b.HasIndex(a => new { a.EntityId, a.CreatedAt });
    }
}

internal class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(n => n.UserId);
        b.HasIndex(n => new { n.UserId, n.CreatedAt });
        b.Property(n => n.Title).HasMaxLength(100);
        b.Property(n => n.Body).HasMaxLength(500);
    }
}
