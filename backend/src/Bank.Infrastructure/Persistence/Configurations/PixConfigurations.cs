using Bank.Domain.Accounts;
using Bank.Domain.Pix;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class PixKeyConfiguration : IEntityTypeConfiguration<PixKey>
{
    public void Configure(EntityTypeBuilder<PixKey> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(k => k.AccountId);
        // A key points to exactly one account in the whole bank.
        b.HasIndex(k => k.Value).IsUnique();
        b.HasIndex(k => k.AccountId);
        b.Property(k => k.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(k => k.Value).HasMaxLength(100);
    }
}

internal class PixTransferConfiguration : IEntityTypeConfiguration<PixTransfer>
{
    public void Configure(EntityTypeBuilder<PixTransfer> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_pix_transfers_refund_within_amount", "refunded_amount >= 0 AND refunded_amount <= amount"));
        b.HasOne<Account>().WithMany().HasForeignKey(p => p.PayerAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(p => p.PayeeAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.EndToEndId).IsUnique();
        b.HasIndex(p => p.LedgerTransactionId).IsUnique();
        // Limit checks sum what a payer sent in a time window.
        b.HasIndex(p => new { p.PayerAccountId, p.CreatedAt });
        b.HasIndex(p => new { p.ScheduleId, p.ScheduleOccurrence }).IsUnique().HasFilter("schedule_id IS NOT NULL");
        b.Property(p => p.EndToEndId).HasMaxLength(32);
        b.Property(p => p.Amount).HasPrecision(18, 2);
        b.Property(p => p.RefundedAmount).HasPrecision(18, 2);
        b.Property(p => p.Description).HasMaxLength(140);
        b.Property(p => p.KeyType).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.KeyValue).HasMaxLength(100);
        b.Ignore(p => p.RefundableAmount);
        b.HasMany(p => p.Refunds).WithOne().HasForeignKey(r => r.PixTransferId);
        b.Navigation(p => p.Refunds).HasField("_refunds");
    }
}

internal class PixRefundConfiguration : IEntityTypeConfiguration<PixRefund>
{
    public void Configure(EntityTypeBuilder<PixRefund> b)
    {
        b.HasIndex(r => r.EndToEndId).IsUnique();
        b.HasIndex(r => r.LedgerTransactionId).IsUnique();
        b.Property(r => r.EndToEndId).HasMaxLength(32);
        b.Property(r => r.Amount).HasPrecision(18, 2);
        b.Property(r => r.Reason).HasMaxLength(140);
    }
}

internal class PixLimitSettingsConfiguration : IEntityTypeConfiguration<PixLimitSettings>
{
    public void Configure(EntityTypeBuilder<PixLimitSettings> b)
    {
        b.HasKey(l => l.AccountId);
        b.HasOne<Account>().WithOne().HasForeignKey<PixLimitSettings>(l => l.AccountId);
        b.Property(l => l.DaytimeLimit).HasPrecision(18, 2);
        b.Property(l => l.NighttimeLimit).HasPrecision(18, 2);
        b.Property(l => l.PendingDaytimeLimit).HasPrecision(18, 2);
        b.Property(l => l.PendingNighttimeLimit).HasPrecision(18, 2);
    }
}

internal class PixScheduleConfiguration : IEntityTypeConfiguration<PixSchedule>
{
    public void Configure(EntityTypeBuilder<PixSchedule> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(s => s.PayerAccountId);
        // The job scans active schedules by date.
        b.HasIndex(s => new { s.Status, s.NextRunDate });
        b.Property(s => s.KeyType).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.KeyValue).HasMaxLength(100);
        b.Property(s => s.Amount).HasPrecision(18, 2);
        b.Property(s => s.Description).HasMaxLength(140);
        b.Property(s => s.Frequency).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.LastError).HasMaxLength(300);
    }
}

internal class PixContactConfiguration : IEntityTypeConfiguration<PixContact>
{
    public void Configure(EntityTypeBuilder<PixContact> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(c => c.OwnerUserId);
        b.HasIndex(c => new { c.OwnerUserId, c.KeyValue }).IsUnique();
        b.Property(c => c.Nickname).HasMaxLength(50);
        b.Property(c => c.KeyType).HasConversion<string>().HasMaxLength(10);
        b.Property(c => c.KeyValue).HasMaxLength(100);
    }
}
