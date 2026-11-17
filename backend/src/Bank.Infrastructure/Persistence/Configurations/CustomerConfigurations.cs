using Bank.Domain.Customers;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.HasOne<User>().WithOne().HasForeignKey<Customer>(c => c.UserId);
        b.HasIndex(c => c.UserId).IsUnique();
        // One account per person; partial so customers still in onboarding (no CPF yet) don't collide.
        b.HasIndex(c => c.Cpf).IsUnique().HasFilter("cpf IS NOT NULL");
        b.HasIndex(c => new { c.KycStatus, c.SubmittedAt });
        b.Property(c => c.FullName).HasMaxLength(100);
        b.Property(c => c.Cpf).HasMaxLength(11).IsFixedLength();
        b.Property(c => c.Phone).HasMaxLength(20);
        b.Property(c => c.MonthlyIncome).HasPrecision(18, 2);
        b.Property(c => c.Occupation).HasMaxLength(100);
        b.Property(c => c.KycStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.KycRejectionReason).HasMaxLength(300);
        b.Ignore(c => c.HasAllDocuments);

        b.OwnsOne(c => c.Address, a =>
        {
            a.Property(x => x.Cep).HasMaxLength(8);
            a.Property(x => x.Street).HasMaxLength(150);
            a.Property(x => x.Number).HasMaxLength(20);
            a.Property(x => x.Complement).HasMaxLength(100);
            a.Property(x => x.Neighborhood).HasMaxLength(100);
            a.Property(x => x.City).HasMaxLength(100);
            a.Property(x => x.State).HasMaxLength(2);
        });

        b.HasMany(c => c.Documents).WithOne().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Documents).HasField("_documents");
    }
}

internal class KycDocumentConfiguration : IEntityTypeConfiguration<KycDocument>
{
    public void Configure(EntityTypeBuilder<KycDocument> b)
    {
        b.Property(d => d.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.StoragePath).HasMaxLength(300);
        b.Property(d => d.ContentType).HasMaxLength(50);
    }
}
