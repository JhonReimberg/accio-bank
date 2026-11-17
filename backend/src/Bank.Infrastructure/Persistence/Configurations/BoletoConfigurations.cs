using Bank.Domain.Accounts;
using Bank.Domain.Boletos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class BoletoConfiguration : IEntityTypeConfiguration<Boleto>
{
    public void Configure(EntityTypeBuilder<Boleto> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.BeneficiaryAccountId);
        b.HasIndex(x => x.Barcode).IsUnique();
        b.HasIndex(x => new { x.BeneficiaryAccountId, x.CreatedAt });
        b.Property(x => x.BeneficiaryName).HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Description).HasMaxLength(140);
        b.Property(x => x.Barcode).HasMaxLength(44);
        b.Property(x => x.DigitableLine).HasMaxLength(47);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
    }
}

internal class BoletoPaymentConfiguration : IEntityTypeConfiguration<BoletoPayment>
{
    public void Configure(EntityTypeBuilder<BoletoPayment> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.PayerAccountId);
        b.HasIndex(x => new { x.Status, x.ScheduledFor });
        b.HasIndex(x => new { x.PayerAccountId, x.CreatedAt });
        b.Property(x => x.Barcode).HasMaxLength(44);
        b.Property(x => x.BeneficiaryName).HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.FailureReason).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
    }
}
