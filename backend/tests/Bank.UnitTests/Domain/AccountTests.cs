using Bank.Domain.Accounts;
using Bank.Domain.Common;

namespace Bank.UnitTests.Domain;

public class AccountTests
{
    private static Account NewAccount(decimal balance = 0)
    {
        var account = Account.Open(Guid.NewGuid(), DateTime.UtcNow);
        if (balance > 0) account.Credit(balance);
        return account;
    }

    [Fact]
    public void Debit_reduces_balance()
    {
        var account = NewAccount(100);
        account.Debit(40.5m);
        Assert.Equal(59.5m, account.Balance);
    }

    [Fact]
    public void Debit_more_than_balance_is_rejected_and_balance_is_unchanged()
    {
        var account = NewAccount(10);
        var ex = Assert.Throws<DomainException>(() => account.Debit(10.01m));
        Assert.Equal("account.insufficient_funds", ex.Code);
        Assert.Equal(10m, account.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Invalid_amounts_are_rejected(decimal amount)
    {
        var account = NewAccount(100);
        Assert.Throws<DomainException>(() => account.Credit(amount));
        Assert.Throws<DomainException>(() => account.Debit(amount));
    }
}
