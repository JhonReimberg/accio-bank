using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.FamilyId);
        b.HasIndex(t => new { t.UserId, t.RevokedAt });
        b.Property(t => t.TokenHash).HasMaxLength(64);
        b.Property(t => t.RevokedReason).HasMaxLength(30);
    }
}

internal class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(d => d.UserId);
        b.HasIndex(d => new { d.UserId, d.DeviceKey }).IsUnique();
        b.Property(d => d.DeviceKey).HasMaxLength(100);
        b.Property(d => d.Name).HasMaxLength(100);
        b.Property(d => d.LastIpAddress).HasMaxLength(64);
    }
}

internal class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.HasIndex(a => new { a.UserId, a.CreatedAt });
        b.Property(a => a.Email).HasMaxLength(200);
        b.Property(a => a.FailureReason).HasMaxLength(50);
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.Property(a => a.UserAgent).HasMaxLength(300);
        b.Property(a => a.DeviceKey).HasMaxLength(100);
    }
}
