using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;
using Bank.Domain.Ledger;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Accounts;

public enum StatementDirection
{
    In,
    Out,
}

/// <param name="From">First day, inclusive (Brasília date).</param>
/// <param name="To">Last day, inclusive (Brasília date).</param>
public record StatementQuery(
    DateOnly? From = null, DateOnly? To = null, TransactionKind? Kind = null, StatementDirection? Direction = null,
    string? Search = null, int Page = 1, int PageSize = 20);

public record StatementItem(
    Guid TransactionId, TransactionKind Kind, EntryDirection Direction, decimal Amount, string Description,
    string? Counterparty, decimal BalanceAfter, DateTime CreatedAt);

public record ReceiptParty(string Name, string? MaskedCpf, string? AccountNumber);

public record ReceiptData(
    Guid TransactionId, TransactionKind Kind, decimal Amount, DateTime CreatedAt, string Description,
    ReceiptParty? Payer, ReceiptParty? Payee, IReadOnlyDictionary<string, string> Details);

public interface IReceiptRenderer
{
    byte[] RenderPdf(ReceiptData receipt);
}

/// <summary>Adds kind-specific lines to a receipt (e.g. PIX end-to-end id). Implemented by feature modules.</summary>
public interface IReceiptDetailsProvider
{
    Task AddDetailsAsync(Guid transactionId, IDictionary<string, string> details, CancellationToken ct);
}

public class StatementQueryValidator : AbstractValidator<StatementQuery>
{
    public StatementQueryValidator()
    {
        RuleFor(x => x).Must(q => q.From is null || q.To is null || q.From <= q.To)
            .WithName("Período").WithMessage("A data inicial deve ser anterior à final.");
        RuleFor(x => x.Search).MaximumLength(100).WithName("Busca");
    }
}

public class StatementService(
    IBankDbContext db,
    ICurrentUser currentUser,
    IReceiptRenderer renderer,
    IEnumerable<IReceiptDetailsProvider> detailProviders,
    IValidator<StatementQuery> validator)
{
    public async Task<PagedResult<StatementItem>> GetAsync(StatementQuery q, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(q, ct);
        var accountId = await MyAccountIdAsync(ct);

        var entries = db.LedgerEntries.Where(e => e.AccountId == accountId);
        if (q.From is { } from)
        {
            var start = BankTime.ToUtc(from.ToDateTime(TimeOnly.MinValue));
            entries = entries.Where(e => e.CreatedAt >= start);
        }
        if (q.To is { } to)
        {
            var end = BankTime.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            entries = entries.Where(e => e.CreatedAt < end);
        }
        if (q.Direction is { } direction)
            entries = entries.Where(e => e.Direction == (direction == StatementDirection.In ? EntryDirection.Credit : EntryDirection.Debit));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            entries = entries.Where(e => e.Description.ToLower().Contains(term));
        }

        var query =
            from e in entries
            join t in db.LedgerTransactions on e.TransactionId equals t.Id
            where q.Kind == null || t.Kind == q.Kind
            orderby e.CreatedAt descending, e.Id descending
            select new StatementItem(
                t.Id, t.Kind, e.Direction,
                e.Direction == EntryDirection.Credit ? e.Amount : -e.Amount,
                e.Description,
                // The other customer of the transaction, if any (deposits only touch the cash account).
                (from o in db.LedgerEntries
                 join a in db.Accounts on o.AccountId equals a.Id
                 join u in db.Users on a.UserId equals u.Id
                 where o.TransactionId == e.TransactionId && o.AccountId != accountId
                 select u.Name).FirstOrDefault(),
                e.BalanceAfter, e.CreatedAt);
        return await query.ToPagedAsync(q.Page, q.PageSize, ct);
    }

    /// <summary>PDF receipt of a transaction the caller's account took part in.</summary>
    public async Task<byte[]> GetReceiptPdfAsync(Guid transactionId, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var entries = await db.LedgerEntries.Where(e => e.TransactionId == transactionId).ToListAsync(ct);
        var mine = entries.SingleOrDefault(e => e.AccountId == accountId)
            ?? throw new NotFoundException("Comprovante não encontrado.");
        var transaction = await db.LedgerTransactions.SingleAsync(t => t.Id == transactionId, ct);

        var parties = await (
                from a in db.Accounts
                where entries.Select(e => e.AccountId).Contains(a.Id)
                join u in db.Users on a.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                join c in db.Customers on a.UserId equals c.UserId into customers
                from c in customers.DefaultIfEmpty()
                select new { a.Id, a.Type, a.Number, a.SystemName, UserName = u != null ? u.Name : null, FullName = c != null ? c.FullName : null, Cpf = c != null ? c.Cpf : null })
            .ToListAsync(ct);

        ReceiptParty? Party(EntryDirection direction)
        {
            var entry = entries.FirstOrDefault(e => e.Direction == direction);
            var p = entry is null ? null : parties.Single(x => x.Id == entry.AccountId);
            if (p is null) return null;
            return p.Type == AccountType.System
                ? new ReceiptParty($"AccioBank ({p.SystemName})", null, null)
                : new ReceiptParty(p.FullName ?? p.UserName ?? "", p.Cpf is null ? null : Cpf.Mask(p.Cpf), p.Number.ToString());
        }

        var details = new Dictionary<string, string>();
        foreach (var provider in detailProviders) await provider.AddDetailsAsync(transactionId, details, ct);

        return renderer.RenderPdf(new ReceiptData(
            transaction.Id, transaction.Kind, mine.Amount, transaction.CreatedAt, mine.Description,
            Party(EntryDirection.Debit), Party(EntryDirection.Credit), details));
    }

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var id = await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
        return id ?? throw new UnauthorizedException("Conta não encontrada.");
    }
}
