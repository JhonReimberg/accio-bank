using Bank.Domain.Accounts;
using Bank.Domain.Auditing;
using Bank.Domain.Credit;
using Bank.Domain.Customers;
using Bank.Domain.Investments;
using Bank.Domain.Ledger;
using Bank.Domain.Notifications;
using Bank.Domain.Pix;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bank.Application.Abstractions;

public interface IBankDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Device> Devices { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<BalanceHold> BalanceHolds { get; }
    DbSet<LedgerTransaction> LedgerTransactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<PixKey> PixKeys { get; }
    DbSet<PixTransfer> PixTransfers { get; }
    DbSet<PixLimitSettings> PixLimits { get; }
    DbSet<PixSchedule> PixSchedules { get; }
    DbSet<PixContact> PixContacts { get; }
    DbSet<CreditPolicy> CreditPolicies { get; }
    DbSet<CreditScore> CreditScores { get; }
    DbSet<Loan> Loans { get; }
    DbSet<LoanInstallment> LoanInstallments { get; }
    DbSet<Investment> Investments { get; }
    DbSet<Domain.Cards.Card> Cards { get; }
    DbSet<Domain.Cards.CreditLine> CreditLines { get; }
    DbSet<Domain.Cards.CardPurchase> CardPurchases { get; }
    DbSet<Domain.Fraud.FraudPolicy> FraudPolicies { get; }
    DbSet<Domain.Fraud.FraudReview> FraudReviews { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Forgets every pending change (after a failed operation, before recording the failure).</summary>
    void DiscardChanges();

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);

    /// <summary>
    /// Loads the accounts with a row lock (SELECT ... FOR UPDATE), always in id order so that two
    /// operations touching the same pair of accounts cannot deadlock. Must run inside a transaction.
    /// </summary>
    Task<IReadOnlyList<Account>> LockAccountsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>
    /// Loads the user with a row lock. Used around attempt counters (password, 2FA, PIN) so parallel
    /// requests cannot each read "0 failures" and bypass the lockout. Must run inside a transaction.
    /// </summary>
    Task<User?> LockUserAsync(Guid id, CancellationToken ct);
}
