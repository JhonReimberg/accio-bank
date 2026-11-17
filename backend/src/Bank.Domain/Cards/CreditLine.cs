using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Cards;

[JsonConverter(typeof(JsonStringEnumConverter<InvoiceStatus>))]
public enum InvoiceStatus
{
    /// <summary>Receiving purchases.</summary>
    Open,
    /// <summary>Closed, waiting for payment until the due date.</summary>
    Closed,
    Paid,
    /// <summary>Due date passed without full payment: the rest went to the next invoice (rotativo).</summary>
    Revolved,
}

[JsonConverter(typeof(JsonStringEnumConverter<InvoiceItemType>))]
public enum InvoiceItemType { Purchase, PreviousBalance, RevolvingInterest, LateFee }

/// <summary>
/// The credit card account: one limit shared by all credit cards of the customer, a due day chosen by
/// the customer and invoices that close 7 days before it.
/// </summary>
public class CreditLine : Entity
{
    public const int DaysBetweenClosingAndDue = 7;
    /// <summary>Revolving credit interest per month (rotativo).</summary>
    public const decimal RevolvingMonthlyRate = 0.12m;
    /// <summary>Minimum payment: 15% of the invoice.</summary>
    public const decimal MinimumPaymentRate = 0.15m;

    private readonly List<Invoice> _invoices = [];

    private CreditLine() { }

    public Guid AccountId { get; private set; }
    public decimal Limit { get; private set; }
    /// <summary>Everything bought and not yet paid (all open, closed and future invoices).</summary>
    public decimal Used { get; private set; }
    public int DueDay { get; private set; }
    public IReadOnlyList<Invoice> Invoices => _invoices;

    public decimal Available => Limit - Used;

    public static CreditLine Open(Guid accountId, decimal limit, int dueDay)
    {
        if (dueDay is < 1 or > 28) throw new DomainException("card.due_day", "Escolha um dia de vencimento entre 1 e 28.");
        Money.EnsureValidAmount(limit);
        return new CreditLine { AccountId = accountId, Limit = limit, DueDay = dueDay };
    }

    public void ChangeLimit(decimal limit, decimal maxLimit)
    {
        if (limit < 0 || limit > maxLimit)
            throw new DomainException("card.limit", $"O limite deve ficar entre {Money.Brl(0)} e {Money.Brl(maxLimit)}.");
        Limit = limit;
    }

    /// <summary>Invoice whose closing date is after <paramref name="date"/>, offset by <paramref name="monthsAhead"/> (installments).</summary>
    public Invoice InvoiceFor(DateOnly date, int monthsAhead = 0)
    {
        var due = new DateOnly(date.Year, date.Month, DueDay);
        while (due.AddDays(-DaysBetweenClosingAndDue) <= date) due = due.AddMonths(1);
        due = due.AddMonths(monthsAhead);
        var invoice = _invoices.SingleOrDefault(i => i.DueDate == due);
        if (invoice is null)
        {
            invoice = Invoice.Create(Id, due, due.AddDays(-DaysBetweenClosingAndDue));
            _invoices.Add(invoice);
        }
        return invoice;
    }

    /// <summary>Spreads a purchase over N invoices: equal installments, the first absorbs the rounding cents.</summary>
    public void RegisterPurchase(Guid purchaseId, string merchant, decimal amount, int installments, DateOnly date)
    {
        Money.EnsureValidAmount(amount);
        if (installments is < 1 or > 12) throw new DomainException("card.installments", "Parcele em até 12 vezes.");
        if (amount > Available) throw new DomainException("card.limit_exceeded", $"Limite disponível: {Money.Brl(Available)}.");

        var each = Money.Round(amount / installments);
        for (var k = 1; k <= installments; k++)
        {
            var value = k == 1 ? amount - each * (installments - 1) : each;
            var label = installments == 1 ? merchant : $"{merchant} ({k}/{installments})";
            InvoiceFor(date, k - 1).AddItem(InvoiceItemType.Purchase, label, value, purchaseId);
        }
        Used += amount;
    }

    /// <summary>Daily closing: invoices whose closing date arrived stop receiving purchases.</summary>
    public IReadOnlyList<Invoice> CloseDue(DateOnly today)
    {
        var closing = _invoices.Where(i => i.Status == InvoiceStatus.Open && i.ClosingDate <= today).ToList();
        foreach (var invoice in closing) invoice.Close();
        return closing;
    }

    public decimal Pay(Invoice invoice, decimal amount)
    {
        Money.EnsureValidAmount(amount);
        var paid = invoice.Pay(amount);
        Used -= paid;
        return paid;
    }

    /// <summary>
    /// Due date passed with a balance left: it moves to the next invoice with revolving interest (and a 2% fine
    /// if not even the minimum was paid). Returns the interest + fine charged (bank revenue).
    /// </summary>
    public decimal Revolve(Invoice invoice, DateOnly today)
    {
        if (invoice.Status != InvoiceStatus.Closed || invoice.DueDate >= today) return 0;
        var remaining = invoice.Remaining;
        var interest = Money.Round(remaining * RevolvingMonthlyRate);
        var fine = invoice.PaidAmount < invoice.MinimumPayment ? Money.Round(remaining * 0.02m) : 0;
        invoice.MarkRevolved();
        var next = InvoiceFor(today);
        next.AddItem(InvoiceItemType.PreviousBalance, $"Saldo da fatura de {invoice.DueDate:MM/yyyy}", remaining, null);
        if (interest > 0) next.AddItem(InvoiceItemType.RevolvingInterest, "Juros do rotativo", interest, null);
        if (fine > 0) next.AddItem(InvoiceItemType.LateFee, "Multa por atraso", fine, null);
        // The remaining was already counted in Used; only the new charges add to it.
        Used += interest + fine;
        return interest + fine;
    }
}

public class Invoice : Entity
{
    private readonly List<InvoiceItem> _items = [];

    private Invoice() { }

    public Guid CreditLineId { get; private set; }
    public DateOnly ClosingDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal PaidAmount { get; private set; }
    public IReadOnlyList<InvoiceItem> Items => _items;

    public decimal Total => _items.Sum(i => i.Amount);
    public decimal Remaining => Total - PaidAmount;
    public decimal MinimumPayment => Money.Round(Total * CreditLine.MinimumPaymentRate);

    internal static Invoice Create(Guid creditLineId, DateOnly due, DateOnly closing) =>
        new() { CreditLineId = creditLineId, DueDate = due, ClosingDate = closing, Status = InvoiceStatus.Open };

    internal void AddItem(InvoiceItemType type, string description, decimal amount, Guid? purchaseId)
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Revolved) throw new InvalidOperationException("Invoice already settled.");
        _items.Add(InvoiceItem.Create(Id, type, description, amount, purchaseId));
    }

    internal void Close() => Status = InvoiceStatus.Closed;

    /// <summary>Total, partial or minimum: anything up to what is left. Paying an open invoice early is allowed.</summary>
    internal decimal Pay(decimal amount)
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Revolved) throw new DomainException("invoice.settled", "Esta fatura já foi quitada.");
        if (amount > Remaining) throw new DomainException("invoice.overpay", $"O valor em aberto é {Money.Brl(Remaining)}.");
        PaidAmount += amount;
        if (Remaining == 0 && Status == InvoiceStatus.Closed) Status = InvoiceStatus.Paid;
        return amount;
    }

    internal void MarkRevolved() => Status = InvoiceStatus.Revolved;
}

public class InvoiceItem : Entity
{
    private InvoiceItem() { }

    public Guid InvoiceId { get; private set; }
    public InvoiceItemType Type { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
    public Guid? PurchaseId { get; private set; }

    internal static InvoiceItem Create(Guid invoiceId, InvoiceItemType type, string description, decimal amount, Guid? purchaseId) =>
        new() { InvoiceId = invoiceId, Type = type, Description = description, Amount = amount, PurchaseId = purchaseId };
}

/// <summary>Authorization record of a card purchase (approved or declined).</summary>
public class CardPurchase : Entity
{
    private CardPurchase() { }

    public Guid CardId { get; private set; }
    public Guid AccountId { get; private set; }
    public string Merchant { get; private set; } = "";
    public decimal Amount { get; private set; }
    public int Installments { get; private set; }
    public bool Online { get; private set; }
    public bool Approved { get; private set; }
    public string? DeclineReason { get; private set; }
    public decimal RoundUp { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static CardPurchase Record(Guid cardId, Guid accountId, string merchant, decimal amount, int installments, bool online, DateTime now) =>
        new() { CardId = cardId, AccountId = accountId, Merchant = merchant, Amount = amount, Installments = installments, Online = online, CreatedAt = now, Approved = true };

    public void Decline(string reason) => (Approved, DeclineReason) = (false, reason);

    public void SetRoundUp(decimal value) => RoundUp = value;
}
