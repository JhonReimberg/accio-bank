using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Data;

public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>("account_numbers").StartsAt(10_000_001);

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(200);
            e.HasOne(u => u.Account).WithOne(a => a.User).HasForeignKey<Account>(a => a.UserId);
        });

        b.Entity<Account>(e =>
        {
            e.ToTable("accounts", t => t.HasCheckConstraint("ck_accounts_balance_non_negative", "\"Balance\" >= 0"));
            e.HasIndex(a => a.Number).IsUnique();
            e.Property(a => a.Number).HasDefaultValueSql("nextval('account_numbers')");
            e.Property(a => a.Balance).HasPrecision(18, 2);
        });

        b.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasIndex(t => new { t.AccountId, t.CreatedAt });
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Description).HasMaxLength(200);
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        });
    }
}
