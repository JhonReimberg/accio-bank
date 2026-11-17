using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Application.Investments;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Cards;
using Bank.Domain.Common;
using Bank.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Cards;

public record IssueCardRequest(CardFunction Function, CardForm Form);

public record OpenCreditLineRequest(int DueDay);

public record CardResponse(
    Guid Id, CardFunction Function, CardForm Form, string Masked, string HolderName, string Expiry, CardStatus Status, bool OnlinePurchasesEnabled);

public record CardDetailsResponse(string Number, string Expiry, string HolderName);

public record CardSettingsRequest(bool? OnlinePurchasesEnabled, bool? Blocked);

public record PurchaseRequest(string Merchant, decimal Amount, int Installments = 1, bool Online = true);

public record PurchaseResponse(Guid Id, bool Approved, string? DeclineReason, decimal Amount, int Installments, decimal RoundUp);

public record InvoiceItemResponse(InvoiceItemType Type, string Description, decimal Amount);

public record InvoiceResponse(
    Guid Id, DateOnly ClosingDate, DateOnly DueDate, InvoiceStatus Status, decimal Total, decimal PaidAmount, decimal Remaining,
    decimal MinimumPayment, IReadOnlyList<InvoiceItemResponse> Items);

public record CreditLineResponse(decimal Limit, decimal Used, decimal Available, int DueDay, IReadOnlyList<InvoiceResponse> Invoices);

public record PayInvoiceRequest(decimal Amount, string Pin);

public record ChangeLimitRequest(decimal Limit, string Pin);

public class CardService(
    IBankDbContext db,
    ICurrentUser currentUser,
    CreditData creditData,
    ScoreService scores,
    LedgerPosting ledger,
    TransactionAuthorizer authorizer,
    IServiceScopeFactory scopes,
    IAuditLog audit,
    TimeProvider clock,
    ILogger<CardService> logger)
{
    public async Task<IReadOnlyList<CardResponse>> ListAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var cards = await db.Cards.AsNoTracking().Where(c => c.AccountId == accountId).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return cards.Select(ToResponse).ToList();
    }

    public async Task<CardResponse> IssueAsync(IssueCardRequest req, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        if (req.Function == CardFunction.Credit && !await db.CreditLines.AnyAsync(l => l.AccountId == accountId, ct))
            throw new DomainException("card.no_credit_line", "Peça seu cartão de crédito (limite) antes de emitir cartões de crédito.");
        if (req.Form == CardForm.Physical && await db.Cards.AnyAsync(c => c.AccountId == accountId && c.Form == CardForm.Physical
                && c.Function == req.Function && c.Status != CardStatus.Cancelled, ct))
            throw new ConflictException("Você já tem um cartão físico desta função.");
        var holder = await db.Customers.Where(c => c.UserId == currentUser.UserId).Select(c => c.FullName).SingleAsync(ct) ?? "";
        var card = Card.Issue(accountId, req.Function, req.Form, holder, clock.GetUtcNow().UtcDateTime, Random.Shared);
        db.Cards.Add(card);
        audit.Record("card.issued", nameof(Card), card.Id, after: new { card.Function, card.Form, card.Last4 });
        await db.SaveChangesAsync(ct);
        return ToResponse(card);
    }

    /// <summary>Full number for online use (virtual cards), behind the transaction PIN and audited.</summary>
    public async Task<CardDetailsResponse> RevealAsync(Guid cardId, string pin, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(pin, ct);
        var card = await MyCardAsync(cardId, ct);
        audit.Record("card.details_revealed", nameof(Card), card.Id);
        await db.SaveChangesAsync(ct);
        return new CardDetailsResponse(card.Number, $"{card.ExpiryMonth:00}/{card.ExpiryYear % 100:00}", card.HolderName);
    }

    public async Task<CardResponse> UpdateSettingsAsync(Guid cardId, CardSettingsRequest req, CancellationToken ct)
    {
        var card = await MyCardAsync(cardId, ct);
        if (req.OnlinePurchasesEnabled is { } online) card.SetOnlinePurchases(online);
        if (req.Blocked == true) card.Block();
        if (req.Blocked == false) card.Unblock();
        audit.Record("card.settings_changed", nameof(Card), card.Id, after: req);
        await db.SaveChangesAsync(ct);
        return ToResponse(card);
    }

    // --- Credit line and invoices ---

    /// <summary>Credit card limit offered by score band: half of the declared income for any band, none below the bands.</summary>
    public async Task<decimal> MaxLimitAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        // First credit visit of a new customer: compute the score now instead of waiting for the nightly job.
        if (!await db.CreditScores.AnyAsync(s => s.UserId == userId, ct)
            && await db.Customers.AnyAsync(c => c.UserId == userId && c.KycStatus == Domain.Customers.KycStatus.Approved, ct))
            await scores.RecalculateAsync(userId, ct);
        var profile = await creditData.ProfileAsync(userId, ct);
        if (profile.Account.Status != AccountStatus.Active || profile.Score is not { } score || profile.Policy.BandFor(score) is null) return 0;
        return Money.Round((profile.Customer.MonthlyIncome ?? 0) * 0.5m);
    }

    public async Task<CreditLineResponse> OpenCreditLineAsync(OpenCreditLineRequest req, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        if (await db.CreditLines.AnyAsync(l => l.AccountId == accountId, ct)) throw new ConflictException("Você já tem cartão de crédito.");
        var max = await MaxLimitAsync(ct);
        if (max <= 0) throw new DomainException("card.no_offer", "No momento não há limite de cartão de crédito disponível para você.");
        var line = CreditLine.Open(accountId, max, req.DueDay);
        db.CreditLines.Add(line);
        audit.Record("card.credit_line_opened", nameof(CreditLine), line.Id, after: new { line.Limit, line.DueDay });
        await db.SaveChangesAsync(ct);
        return ToResponse(line);
    }

    public async Task<CreditLineResponse?> GetCreditLineAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var line = await db.CreditLines.Include(l => l.Invoices).ThenInclude(i => i.Items).AsNoTracking()
            .SingleOrDefaultAsync(l => l.AccountId == accountId, ct);
        return line is null ? null : ToResponse(line);
    }

    public async Task<CreditLineResponse> ChangeLimitAsync(ChangeLimitRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var accountId = await MyAccountIdAsync(ct);
        var max = await MaxLimitAsync(ct);
        var line = await LoadLineAsync(accountId, ct);
        var before = line.Limit;
        line.ChangeLimit(req.Limit, Math.Max(max, line.Used));
        audit.Record("card.limit_changed", nameof(CreditLine), line.Id, new { Limit = before }, new { req.Limit });
        await db.SaveChangesAsync(ct);
        return ToResponse(line);
    }

    /// <summary>Total, partial or minimum payment from the checking account.</summary>
    public async Task<CreditLineResponse> PayInvoiceAsync(Guid invoiceId, PayInvoiceRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var accountId = await MyAccountIdAsync(ct);
        CreditLine? line = null;
        await ledger.ExecuteAsync([accountId, SystemAccounts.CardSettlement], async (accounts, now) =>
        {
            line = await LoadLineAsync(accountId, ct);
            var invoice = line.Invoices.SingleOrDefault(i => i.Id == invoiceId) ?? throw new NotFoundException("Fatura não encontrada.");
            line.Pay(invoice, req.Amount);
            audit.Record("card.invoice_paid", nameof(Invoice), invoice.Id, after: new { req.Amount, invoice.Remaining });
            return LedgerTransaction.Transfer(TransactionKind.CardInvoicePayment, accounts[accountId], accounts[SystemAccounts.CardSettlement], req.Amount,
                $"Pagamento da fatura {invoice.DueDate:MM/yyyy}", "Recebimento de fatura", now);
        }, ct);
        return ToResponse(line!);
    }

    // --- Purchases (simulated card network) ---

    /// <summary>
    /// Authorizes a purchase like the card network would: card checks, then debit (checking balance) or credit
    /// (limit, spread over invoices). Declines are recorded, never thrown, as a real authorizer answers "no".
    /// </summary>
    public async Task<PurchaseResponse> PurchaseAsync(Guid cardId, PurchaseRequest req, CancellationToken ct)
    {
        var card = await MyCardAsync(cardId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var purchase = CardPurchase.Record(card.Id, card.AccountId, req.Merchant.Trim(), req.Amount, req.Installments, req.Online, now);
        try
        {
            Money.EnsureValidAmount(req.Amount);
            card.EnsureCanPurchase(req.Online, now);
            if (card.Function == CardFunction.Debit)
            {
                if (req.Installments != 1) throw new DomainException("card.installments", "No débito a compra é à vista.");
                // Money leaves the customer to the merchant (paid through the bank's cash/clearing account).
                await ledger.ExecuteAsync([card.AccountId, SystemAccounts.Cash], (accounts, at) =>
                    LedgerTransaction.Transfer(TransactionKind.CardPurchase, accounts[card.AccountId], accounts[SystemAccounts.Cash],
                        req.Amount, $"Compra no débito: {purchase.Merchant}", $"Repasse ao estabelecimento: {purchase.Merchant}", at), ct);
            }
            else
            {
                // The bank pays the merchant and now has a receivable from the customer. The customer's account row
                // is locked too, so two simultaneous purchases cannot both pass the limit check.
                await ledger.ExecuteAsync([card.AccountId, SystemAccounts.CardSettlement, SystemAccounts.Cash], async (accounts, at) =>
                {
                    var line = await LoadLineAsync(card.AccountId, ct);
                    line.RegisterPurchase(purchase.Id, purchase.Merchant, req.Amount, req.Installments, BankTime.Today(at));
                    return LedgerTransaction.Transfer(TransactionKind.CardPurchase, accounts[SystemAccounts.CardSettlement], accounts[SystemAccounts.Cash],
                        req.Amount, $"A receber: compra no crédito em {purchase.Merchant}", $"Repasse ao estabelecimento: {purchase.Merchant}", at);
                }, ct);
            }
            card.PurchaseApproved();
        }
        catch (DomainException e)
        {
            db.DiscardChanges();
            card = await MyCardAsync(cardId, ct);
            purchase.Decline(e.Message);
        }
        db.CardPurchases.Add(purchase);
        audit.Record(purchase.Approved ? "card.purchase_approved" : "card.purchase_declined", nameof(CardPurchase), purchase.Id,
            after: new { purchase.Merchant, purchase.Amount, purchase.Installments, purchase.DeclineReason });
        await db.SaveChangesAsync(ct);

        if (purchase.Approved) await RoundUpAsync(card.AccountId, purchase, ct);
        return new PurchaseResponse(purchase.Id, purchase.Approved, purchase.DeclineReason, purchase.Amount, purchase.Installments, purchase.RoundUp);
    }

    /// <summary>"Arredondar para a caixinha": the cents up to the next real go to the round-up caixinha. Best effort.</summary>
    private async Task RoundUpAsync(Guid accountId, CardPurchase purchase, CancellationToken ct)
    {
        var caixinhaId = await db.Investments.Where(i => i.AccountId == accountId && i.RoundUpCardPurchases).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        var roundUp = Math.Ceiling(purchase.Amount) - purchase.Amount;
        if (caixinhaId is null || roundUp == 0) return;
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<InvestmentService>()
                .ApplyCoreAsync(accountId, caixinhaId.Value, roundUp, "Arredondamento de compra", ct);
            await db.CardPurchases.Where(p => p.Id == purchase.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RoundUp, roundUp), ct);
            purchase.SetRoundUp(roundUp);
        }
        catch (DomainException e)
        {
            logger.LogInformation("Round-up of purchase {PurchaseId} skipped: {Reason}", purchase.Id, e.Message);
        }
    }

    private async Task<CreditLine> LoadLineAsync(Guid accountId, CancellationToken ct) =>
        await db.CreditLines.Include(l => l.Invoices).ThenInclude(i => i.Items).SingleOrDefaultAsync(l => l.AccountId == accountId, ct)
        ?? throw new DomainException("card.no_credit_line", "Você ainda não tem cartão de crédito.");

    private async Task<Card> MyCardAsync(Guid cardId, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        return await db.Cards.SingleOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId, ct) ?? throw new NotFoundException("Cartão não encontrado.");
    }

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("Conta não encontrada.");
    }

    private static CardResponse ToResponse(Card c) =>
        new(c.Id, c.Function, c.Form, c.Masked, c.HolderName, $"{c.ExpiryMonth:00}/{c.ExpiryYear % 100:00}", c.Status, c.OnlinePurchasesEnabled);

    private static CreditLineResponse ToResponse(CreditLine l) => new(
        l.Limit, l.Used, l.Available, l.DueDay,
        l.Invoices.OrderBy(i => i.DueDate).Select(i => new InvoiceResponse(i.Id, i.ClosingDate, i.DueDate, i.Status, i.Total, i.PaidAmount,
            i.Remaining, i.MinimumPayment, i.Items.Select(x => new InvoiceItemResponse(x.Type, x.Description, x.Amount)).ToList())).ToList());
}

/// <summary>
/// Daily: closes invoices on their closing date and moves unpaid balances of past-due invoices to the next
/// one with revolving interest. The interest and fine become bank revenue against card receivables.
/// </summary>
public class CardInvoiceJob(IBankDbContext db, LedgerPosting ledger, TimeProvider clock, ILogger<CardInvoiceJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var today = BankTime.Today(clock.GetUtcNow().UtcDateTime);
        var lines = await db.CreditLines.Include(l => l.Invoices).ThenInclude(i => i.Items).ToListAsync(ct);
        decimal revenue = 0;
        foreach (var line in lines)
        {
            line.CloseDue(today);
            foreach (var invoice in line.Invoices.Where(i => i.Status == InvoiceStatus.Closed && i.DueDate < today).ToList())
                revenue += line.Revolve(invoice, today);
        }
        await db.SaveChangesAsync(ct);
        if (revenue > 0)
            await ledger.ExecuteAsync([SystemAccounts.CardSettlement, SystemAccounts.CreditRevenue], (accounts, now) =>
                LedgerTransaction.Transfer(TransactionKind.CardInterest, accounts[SystemAccounts.CardSettlement], accounts[SystemAccounts.CreditRevenue],
                    revenue, "Juros e multas do rotativo a receber", "Juros e multas do rotativo", now), ct);
        logger.LogInformation("Card invoices processed: {Lines} credit lines, revolving charges {Revenue}", lines.Count, revenue);
    }
}
