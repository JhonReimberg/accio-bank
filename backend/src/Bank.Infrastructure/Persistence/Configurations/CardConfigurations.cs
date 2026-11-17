using Bank.Domain.Accounts;
using Bank.Domain.Cards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(c => c.AccountId);
        b.HasIndex(c => c.Number).IsUnique();
        b.HasIndex(c => c.AccountId);
        // shortcut: the PAN is stored in clear for the demo; a real issuer keeps it in a PCI-scoped vault/tokenizer.
        b.Property(c => c.Number).HasMaxLength(19);
        b.Property(c => c.HolderName).HasMaxLength(100);
        b.Property(c => c.Function).HasConversion<string>().HasMaxLength(10);
        b.Property(c => c.Form).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
        b.Ignore(c => c.Last4);
        b.Ignore(c => c.Masked);
    }
}

internal class CreditLineConfiguration : IEntityTypeConfiguration<CreditLine>
{
    public void Configure(EntityTypeBuilder<CreditLine> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_credit_lines_used_within_limit_or_charges", "used >= 0"));
        b.HasOne<Account>().WithOne().HasForeignKey<CreditLine>(l => l.AccountId);
        b.Property(l => l.Limit).HasPrecision(18, 2);
        b.Property(l => l.Used).HasPrecision(18, 2);
        b.Ignore(l => l.Available);
        b.HasMany(l => l.Invoices).WithOne().HasForeignKey(i => i.CreditLineId);
        b.Navigation(l => l.Invoices).HasField("_invoices");
    }
}

internal class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.HasIndex(i => new { i.CreditLineId, i.DueDate }).IsUnique();
        b.HasIndex(i => new { i.Status, i.ClosingDate });
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(i => i.PaidAmount).HasPrecision(18, 2);
        foreach (var name in new[] { nameof(Invoice.Total), nameof(Invoice.Remaining), nameof(Invoice.MinimumPayment) }) b.Ignore(name);
        b.HasMany(i => i.Items).WithOne().HasForeignKey(x => x.InvoiceId);
        b.Navigation(i => i.Items).HasField("_items");
    }
}

internal class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> b)
    {
        b.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Description).HasMaxLength(150);
        b.Property(i => i.Amount).HasPrecision(18, 2);
    }
}

internal class CardPurchaseConfiguration : IEntityTypeConfiguration<CardPurchase>
{
    public void Configure(EntityTypeBuilder<CardPurchase> b)
    {
        b.HasIndex(p => new { p.AccountId, p.CreatedAt });
        b.Property(p => p.Merchant).HasMaxLength(100);
        b.Property(p => p.Amount).HasPrecision(18, 2);
        b.Property(p => p.RoundUp).HasPrecision(18, 2);
        b.Property(p => p.DeclineReason).HasMaxLength(200);
    }
}
