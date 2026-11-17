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
    public void Cannot_hold_more_than_available()
    {
        var account = Customer(10);
        Assert.Throws<DomainException>(() => account.PlaceHold(10.01m, "x", Now));
    }
}
