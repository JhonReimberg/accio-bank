using Bank.Domain.Accounts;
using Bank.Domain.Fraud;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class FraudPolicyConfiguration : IEntityTypeConfiguration<FraudPolicy>
{
    public void Configure(EntityTypeBuilder<FraudPolicy> b)
    {
        b.Property(p => p.Id).ValueGeneratedNever();
        b.ToTable(t => t.HasCheckConstraint("ck_fraud_policies_singleton", "id = 1"));
        b.Property(p => p.AverageMultiplier).HasPrecision(8, 2);
        b.Property(p => p.AverageRuleMinAmount).HasPrecision(18, 2);
        b.Property(p => p.NewDeviceAmount).HasPrecision(18, 2);
    }
}

internal class FraudReviewConfiguration : IEntityTypeConfiguration<FraudReview>
{
    public void Configure(EntityTypeBuilder<FraudReview> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId);
        b.HasIndex(r => new { r.Status, r.CreatedAt });
        b.HasIndex(r => r.UserId);
        b.Property(r => r.Amount).HasPrecision(18, 2);
        b.Property(r => r.PayeeName).HasMaxLength(100);
        b.Property(r => r.KeyType).HasConversion<string>().HasMaxLength(10);
        b.Property(r => r.KeyValue).HasMaxLength(100);
        b.Property(r => r.Description).HasMaxLength(140);
        b.Property(r => r.Reasons).HasMaxLength(500);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
    }
}
