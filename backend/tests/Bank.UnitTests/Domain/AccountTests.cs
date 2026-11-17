using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Ledger;

namespace Bank.UnitTests.Domain;

public class AccountTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    internal static Account Customer(decimal balance = 0)
    {
        var account = Account.OpenCustomer(Guid.NewGuid(), Now);
        account.Activate(AccountTier.Basic, Now);
        if (balance > 0) Deposit(account, balance);
        return account;
    }

    internal static LedgerTransaction Deposit(Account account, decimal amount) =>
        LedgerTransaction.Transfer(TransactionKind.Deposit, Cash(), account, amount, "in", "in", Now);

    internal static Account Cash() => Account.OpenSystem(Guid.NewGuid(), 1, "Caixa", Now);

    [Fact]
    public void Hold_reduces_available_but_not_total_balance()
    {
        var account = Customer(100);
        account.PlaceHold(30, "Bloqueio judicial", Now);
        Assert.Equal(100m, account.Balance);
        Assert.Equal(30m, account.BlockedBalance);
        Assert.Equal(70m, account.AvailableBalance);
    }

    [Fact]
    public void Blocked_funds_cannot_be_spent()
    {
        var account = Customer(100);
        account.PlaceHold(80, "Garantia", Now);
        var ex = Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 20.01m, "out", "in", Now));
        Assert.Equal("account.insufficient_funds", ex.Code);
    }

    [Fact]
    public void Releasing_a_hold_makes_funds_available_again_and_only_once()
    {
        var account = Customer(100);
        var hold = account.PlaceHold(40, "Análise antifraude", Now);
        account.ReleaseHold(hold, Now);
        Assert.Equal(100m, account.AvailableBalance);
        Assert.Throws<DomainException>(() => account.ReleaseHold(hold, Now));
    }

    [Fact]
    public void Accounts_under_review_cannot_send_or_receive()
    {
        var pending = Account.OpenCustomer(Guid.NewGuid(), Now);
        Assert.Equal("account.not_active", Assert.Throws<DomainException>(() => Deposit(pending, 10)).Code);
        Assert.Equal("account.pending_review", Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(TransactionKind.InternalTransfer, pending, Customer(), 1, "x", "y", Now)).Code);
    }

    [Fact]
    public void Blocked_accounts_can_receive_but_not_send()
    {
        var account = Customer(100);
        account.Block("Suspeita de fraude");
        Deposit(account, 10);
        Assert.Equal(110m, account.Balance);
        Assert.Equal("account.blocked", Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 1, "x", "y", Now)).Code);
        account.Unblock();
        LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 1, "x", "y", Now);
    }

    [Fact]
    public void Only_empty_accounts_can_be_closed()
    {
        var account = Customer(1);
        Assert.Throws<DomainException>(() => account.Close("Pedido do cliente"));
        LedgerTransaction.Transfer(TransactionKind.InternalTransfer, account, Customer(), 1, "x", "y", Now);
        account.Close("Pedido do cliente");
        Assert.Equal(AccountStatus.Closed, account.Status);
    }

    [Theory]
    [InlineData(1_500, AccountTier.Basic)]
    [InlineData(4_999.99, AccountTier.Basic)]
    [InlineData(5_000, AccountTier.Plus)]
    [InlineData(15_000, AccountTier.Premium)]
    public void Tier_follows_declared_income(decimal income, AccountTier expected) =>
        Assert.Equal(expected, Account.TierForIncome(income));

    [Fact]
    public void Cannot_hold_more_than_available()
    {
        var account = Customer(10);
        Assert.Throws<DomainException>(() => account.PlaceHold(10.01m, "x", Now));
    }
}
