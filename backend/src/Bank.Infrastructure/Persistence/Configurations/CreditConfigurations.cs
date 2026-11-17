using Bank.Domain.Accounts;
using Bank.Domain.Credit;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Infrastructure.Persistence.Configurations;

internal class CreditPolicyConfiguration : IEntityTypeConfiguration<CreditPolicy>
{
    public void Configure(EntityTypeBuilder<CreditPolicy> b)
    {
        b.Property(p => p.Id).ValueGeneratedNever();
        b.ToTable(t => t.HasCheckConstraint("ck_credit_policies_singleton", "id = 1"));
        foreach (var name in new[] { nameof(CreditPolicy.MinMovement30Days), nameof(CreditPolicy.MinLoanAmount) })
            b.Property(name).HasPrecision(18, 2);
        foreach (var name in new[] { nameof(CreditPolicy.MaxIncomeCommitment), nameof(CreditPolicy.LateFeeRate), nameof(CreditPolicy.DailyLateInterestRate),
                     nameof(CreditPolicy.OverdraftMonthlyRate), nameof(CreditPolicy.CdiAnnualRate), nameof(CreditPolicy.GuaranteedMonthlyRate) })
            b.Property(name).HasPrecision(10, 6);
        b.Ignore(p => p.OverdraftDailyRate);
        // Bands are a value list edited as a whole by the Admin: one JSON column.
        b.OwnsMany(p => p.Bands, bands =>
        {
            bands.ToJson();
            bands.Property(x => x.MonthlyRate).HasPrecision(10, 6);
        });
    }
}

internal class CreditScoreConfiguration : IEntityTypeConfiguration<CreditScore>
{
    public void Configure(EntityTypeBuilder<CreditScore> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId);
        b.HasIndex(s => new { s.UserId, s.CalculatedAt });
        b.Property(s => s.Factors).HasColumnType("jsonb");
    }
}

internal class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> b)
    {
        b.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId);
        b.HasIndex(l => new { l.UserId, l.Status });
        b.HasIndex(l => l.DisbursementTransactionId).IsUnique();
        foreach (var name in new[] { nameof(Loan.Amount), nameof(Loan.InstallmentAmount), nameof(Loan.Iof), nameof(Loan.NetAmount) })
            b.Property(name).HasPrecision(18, 2);
        foreach (var name in new[] { nameof(Loan.MonthlyRate), nameof(Loan.CetMonthly), nameof(Loan.CetYearly) })
            b.Property(name).HasPrecision(10, 6);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        b.Ignore(l => l.OutstandingPrincipal);
        b.HasMany(l => l.Installments).WithOne().HasForeignKey(i => i.LoanId);
        b.Navigation(l => l.Installments).HasField("_installments");
    }
}

internal class LoanInstallmentConfiguration : IEntityTypeConfiguration<LoanInstallment>
{
    public void Configure(EntityTypeBuilder<LoanInstallment> b)
    {
        b.HasIndex(i => new { i.LoanId, i.Number }).IsUnique();
        // Collection job scans unpaid installments by due date.
        b.HasIndex(i => new { i.Status, i.DueDate });
        foreach (var name in new[] { nameof(LoanInstallment.Amount), nameof(LoanInstallment.Interest), nameof(LoanInstallment.Principal),
                     nameof(LoanInstallment.BalanceAfter), nameof(LoanInstallment.PaidAmount), nameof(LoanInstallment.LateFee),
                     nameof(LoanInstallment.LateInterest), nameof(LoanInstallment.Discount) })
            b.Property(name).HasPrecision(18, 2);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
    }
}
