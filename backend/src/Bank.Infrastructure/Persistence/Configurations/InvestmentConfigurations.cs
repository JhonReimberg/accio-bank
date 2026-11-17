using Bank.Domain.Accounts;
using Bank.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class InvestmentConfiguration : IEntityTypeConfiguration<Investment>
{
    public void Configure(EntityTypeBuilder<Investment> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_investments_blocked_non_negative", "blocked_amount >= 0"));
        b.HasOne<Account>().WithMany().HasForeignKey(i => i.AccountId);
        b.HasIndex(i => i.AccountId);
        b.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Name).HasMaxLength(60);
        b.Property(i => i.Goal).HasPrecision(18, 2);
        b.Property(i => i.CdiPercent).HasPrecision(6, 4);
        b.Property(i => i.BlockedAmount).HasPrecision(18, 2);
        foreach (var name in new[] { nameof(Investment.Principal), nameof(Investment.Value), nameof(Investment.Income), nameof(Investment.AvailableToRedeem) })
            b.Ignore(name);
        b.HasMany(i => i.Lots).WithOne().HasForeignKey(l => l.InvestmentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Lots).HasField("_lots");
    }
}

internal class InvestmentLotConfiguration : IEntityTypeConfiguration<InvestmentLot>
{
    public void Configure(EntityTypeBuilder<InvestmentLot> b)
    {
        b.Property(l => l.Principal).HasPrecision(18, 2);
        b.Property(l => l.Value).HasPrecision(18, 2);
    }
}
