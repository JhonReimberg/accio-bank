using Bank.Domain.Accounts;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.Name).HasMaxLength(100);
        b.Property(u => u.Email).HasMaxLength(200);
    }
}

internal class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_accounts_balance_non_negative", "balance >= 0"));
        b.HasOne<User>().WithOne().HasForeignKey<Account>(a => a.UserId);
        b.HasIndex(a => a.UserId).IsUnique();
        b.HasIndex(a => a.Number).IsUnique();
        b.Property(a => a.Number).HasDefaultValueSql("nextval('account_numbers')");
        b.Property(a => a.Balance).HasPrecision(18, 2);
    }
}

internal class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId);
        b.HasIndex(t => new { t.AccountId, t.CreatedAt });
        b.Property(t => t.Amount).HasPrecision(18, 2);
        b.Property(t => t.Description).HasMaxLength(200);
        b.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
    }
}
