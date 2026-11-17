using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Ledger;
using static Bank.UnitTests.Domain.AccountTests;

namespace Bank.UnitTests.Domain;

public class LedgerTransactionTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Transfer_writes_one_debit_and_one_credit_of_the_same_amount()
    {
        var from = Customer(100);
        var to = Customer();

        var tx = LedgerTransaction.Transfer(TransactionKind.InternalTransfer, from, to, 40, "para B", "de A", Now);

        Assert.Equal(2, tx.Entries.Count);
        var debit = Assert.Single(tx.Entries, e => e.Direction == EntryDirection.Debit);
        var credit = Assert.Single(tx.Entries, e => e.Direction == EntryDirection.Credit);
        Assert.Equal((from.Id, 40m, 60m), (debit.AccountId, debit.Amount, debit.BalanceAfter));
        Assert.Equal((to.Id, 40m, 40m), (credit.AccountId, credit.Amount, credit.BalanceAfter));
    }

    [Fact]
    public void Unbalanced_postings_are_rejected()
    {
        var a = Customer(100);
        var b = Customer();
        Assert.Throws<InvalidOperationException>(() => LedgerTransaction.Post(TransactionKind.InternalTransfer, Now,
            new Posting(a, EntryDirection.Debit, 10, "x"),
            new Posting(b, EntryDirection.Credit, 9.99m, "y")));
    }

    [Fact]
    public void Insufficient_funds_leave_both_accounts_untouched()
    {
        var from = Customer(10);
        var to = Customer();
        Assert.Throws<DomainException>(() => LedgerTransaction.Transfer(TransactionKind.InternalTransfer, from, to, 10.01m, "x", "y", Now));
        Assert.Equal(10m, from.Balance);
        Assert.Equal(0m, to.Balance);
    }

    [Fact]
    public void System_accounts_may_go_negative_as_the_counterpart_of_cash_in()
    {
        var cash = Cash();
        var customer = Customer();
        LedgerTransaction.Transfer(TransactionKind.Deposit, cash, customer, 250, "saída caixa", "Depósito", Now);
        Assert.Equal(-250m, cash.Balance);
        Assert.Equal(250m, customer.Balance);
        Assert.Equal(0m, cash.Balance + customer.Balance);
    }

    [Fact]
    public void Multi_leg_transactions_are_supported_when_balanced()
    {
        var payer = Customer(100);
        var payee = Customer();
        var revenue = Account.OpenSystem(Guid.NewGuid(), 2, "Receita", Now);

        var tx = LedgerTransaction.Post(TransactionKind.Pix, Now,
            new Posting(payer, EntryDirection.Debit, 10.50m, "Pagamento"),
            new Posting(payee, EntryDirection.Credit, 10, "Recebimento"),
            new Posting(revenue, EntryDirection.Credit, 0.50m, "Tarifa"));

        Assert.Equal(3, tx.Entries.Count);
        Assert.Equal(89.50m, payer.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Invalid_amounts_are_rejected(decimal amount) =>
        Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(TransactionKind.InternalTransfer, Customer(100), Customer(), amount, "x", "y", Now));
}
