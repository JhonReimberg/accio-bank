using Bank.Domain.Cards;
using Bank.Domain.Common;

namespace Bank.UnitTests.Domain;

public class CardTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("4539578763621486", true)] // well-known valid test number
    [InlineData("4539578763621487", false)]
    [InlineData("1234", false)]
    public void Luhn_validates_card_numbers(string number, bool valid) => Assert.Equal(valid, Luhn.IsValid(number));

    [Fact]
    public void Generated_numbers_pass_luhn_and_use_the_fictitious_bin()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var number = Luhn.Generate(random);
            Assert.True(Luhn.IsValid(number));
            Assert.StartsWith(Luhn.Bin, number);
        }
    }

    [Fact]
    public void Temporary_virtual_card_dies_after_the_first_purchase_and_physical_starts_offline()
    {
        var temporary = Card.Issue(Guid.NewGuid(), CardFunction.Credit, CardForm.VirtualTemporary, "Ana", Now, new Random());
        temporary.EnsureCanPurchase(online: true, Now);
        temporary.PurchaseApproved();
        Assert.Equal("card.cancelled", Assert.Throws<DomainException>(() => temporary.EnsureCanPurchase(true, Now)).Code);

        var physical = Card.Issue(Guid.NewGuid(), CardFunction.Debit, CardForm.Physical, "Ana", Now, new Random());
        Assert.Equal("card.online_disabled", Assert.Throws<DomainException>(() => physical.EnsureCanPurchase(online: true, Now)).Code);
        physical.EnsureCanPurchase(online: false, Now);
    }
}

public class InvoiceTests
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    [Fact]
    public void Purchases_go_to_the_invoice_that_has_not_closed_yet()
    {
        var line = CreditLine.Open(Guid.NewGuid(), 5_000, dueDay: 10); // closes on the 3rd
        Assert.Equal(new DateOnly(2026, 4, 10), line.InvoiceFor(new DateOnly(2026, 3, 15)).DueDate);
        Assert.Equal(new DateOnly(2026, 4, 10), line.InvoiceFor(new DateOnly(2026, 4, 2)).DueDate);
        Assert.Equal(new DateOnly(2026, 5, 10), line.InvoiceFor(new DateOnly(2026, 4, 3)).DueDate);
    }

    [Fact]
    public void Installment_purchases_spread_over_future_invoices_and_use_the_whole_limit_upfront()
    {
        var line = CreditLine.Open(Guid.NewGuid(), 5_000, 10);
        line.RegisterPurchase(Guid.NewGuid(), "Notebook", 1_000, 3, Today);

        Assert.Equal(3, line.Invoices.Count);
        Assert.Equal([333.34m, 333.33m, 333.33m], line.Invoices.OrderBy(i => i.DueDate).Select(i => i.Total));
        Assert.Equal("Notebook (2/3)", line.Invoices.OrderBy(i => i.DueDate).ElementAt(1).Items[0].Description);
        Assert.Equal(4_000m, line.Available);
        Assert.Equal("card.limit_exceeded", Assert.Throws<DomainException>(() => line.RegisterPurchase(Guid.NewGuid(), "TV", 4_000.01m, 1, Today)).Code);
    }

    [Fact]
    public void Partial_payment_revolves_the_rest_with_interest_and_releases_the_paid_limit()
    {
        var line = CreditLine.Open(Guid.NewGuid(), 5_000, 10);
        line.RegisterPurchase(Guid.NewGuid(), "Mercado", 1_000, 1, Today);
        var invoice = line.InvoiceFor(Today);
        line.CloseDue(invoice.ClosingDate);
        Assert.Equal((InvoiceStatus.Closed, 150m), (invoice.Status, invoice.MinimumPayment));

        line.Pay(invoice, 400); // above the minimum: no fine
        var charged = line.Revolve(invoice, invoice.DueDate.AddDays(1));

        Assert.Equal(InvoiceStatus.Revolved, invoice.Status);
        Assert.Equal(72m, charged); // 12% of 600
        var next = line.InvoiceFor(invoice.DueDate.AddDays(1));
        Assert.Equal(672m, next.Total);
        Assert.Equal(5_000m - 672m, line.Available);
    }

    [Fact]
    public void Paying_below_the_minimum_adds_a_late_fee_and_full_payment_settles_the_invoice()
    {
        var line = CreditLine.Open(Guid.NewGuid(), 5_000, 10);
        line.RegisterPurchase(Guid.NewGuid(), "Mercado", 1_000, 1, Today);
        var invoice = line.InvoiceFor(Today);
        line.CloseDue(invoice.ClosingDate);
        Assert.Equal(140m, line.Revolve(invoice, invoice.DueDate.AddDays(1))); // 12% + 2% of 1,000

        var other = CreditLine.Open(Guid.NewGuid(), 5_000, 10);
        other.RegisterPurchase(Guid.NewGuid(), "Mercado", 1_000, 1, Today);
        var paidInFull = other.InvoiceFor(Today);
        other.CloseDue(paidInFull.ClosingDate);
        other.Pay(paidInFull, 1_000);
        Assert.Equal(InvoiceStatus.Paid, paidInFull.Status);
        Assert.Equal(0m, other.Revolve(paidInFull, paidInFull.DueDate.AddDays(1)));
        Assert.Throws<DomainException>(() => other.Pay(paidInFull, 1));
    }
}
