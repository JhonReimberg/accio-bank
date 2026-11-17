using Bank.Domain.Accounts;
using Bank.Domain.Ledger;
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
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.TwoFactorSecret).HasMaxLength(500);
        b.Property(u => u.TransactionPinHash).HasMaxLength(200);
        b.Ignore(u => u.HasTransactionPin);
    }
}

internal class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable(t =>
        {
            // Defense in depth: even a bug in the application cannot take a customer below the overdraft limit.
            t.HasCheckConstraint("ck_accounts_customer_balance_within_overdraft", "type <> 'Customer' OR balance >= -overdraft_limit");
            t.HasCheckConstraint("ck_accounts_blocked_balance_valid",
                "blocked_balance >= 0 AND overdraft_limit >= 0 AND (type <> 'Customer' OR blocked_balance <= balance + overdraft_limit)");
        });
        b.HasOne<User>().WithOne().HasForeignKey<Account>(a => a.UserId);
        b.HasIndex(a => a.UserId).IsUnique();
        b.HasIndex(a => a.Number).IsUnique();
        b.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Tier).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.StatusReason).HasMaxLength(200);
        b.Property(a => a.Number).HasDefaultValueSql("nextval('account_numbers')");
        b.Property(a => a.SystemName).HasMaxLength(100);
        b.Property(a => a.Balance).HasPrecision(18, 2);
        b.Property(a => a.BlockedBalance).HasPrecision(18, 2);
        b.Property(a => a.OverdraftLimit).HasPrecision(18, 2);
        b.Ignore(a => a.AvailableBalance);
        b.Ignore(a => a.SpendingPower);
        b.Ignore(a => a.OverdraftUsed);

        b.HasData(
            System(SystemAccounts.Cash, 1, "Caixa"),
            System(SystemAccounts.LoanPortfolio, 2, "Carteira de crédito"),
            System(SystemAccounts.CreditRevenue, 3, "Receitas de crédito"),
            System(SystemAccounts.IofPayable, 4, "IOF a recolher"),
            System(SystemAccounts.InvestmentCustody, 5, "Custódia de investimentos"),
            System(SystemAccounts.InvestmentYieldExpense, 6, "Despesa com rendimentos"),
            System(SystemAccounts.IncomeTaxPayable, 7, "IR a recolher"),
            System(SystemAccounts.CardSettlement, 8, "Liquidação de cartões"));
    }

    private static object System(Guid id, long number, string name) => new
    {
        Id = id,
        Type = AccountType.System,
        Number = number,
        SystemName = name,
        Status = AccountStatus.Active,
        Tier = AccountTier.Basic,
        Balance = 0m,
        BlockedBalance = 0m,
        OverdraftLimit = 0m,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}

internal class BalanceHoldConfiguration : IEntityTypeConfiguration<BalanceHold>
{
    public void Configure(EntityTypeBuilder<BalanceHold> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(h => h.AccountId);
        b.HasIndex(h => new { h.AccountId, h.Status });
        b.Property(h => h.Amount).HasPrecision(18, 2);
        b.Property(h => h.Reason).HasMaxLength(200);
        b.Property(h => h.Status).HasConversion<string>().HasMaxLength(20);
    }
}

internal class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> b)
    {
        b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        b.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.TransactionId);
        b.Navigation(t => t.Entries).HasField("_entries");
    }
}

internal class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable(t => t.HasCheckConstraint("ck_ledger_entries_amount_positive", "amount > 0"));
        b.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId);
        b.HasIndex(e => new { e.AccountId, e.CreatedAt });
        b.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.BalanceAfter).HasPrecision(18, 2);
        b.Property(e => e.Description).HasMaxLength(200);
        b.Ignore(e => e.SignedAmount);
    }
}
