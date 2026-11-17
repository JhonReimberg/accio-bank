using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Accounts;

public record AccountResponse(string Name, string Email, string Number, decimal Balance);

public record TransactionResponse(Guid Id, TransactionType Type, decimal Amount, string Description, DateTime CreatedAt);

public record DepositRequest(decimal Amount);

/// <param name="To">Account number or the recipient's e-mail.</param>
public record TransferRequest(string To, decimal Amount);

public class DepositRequestValidator : AbstractValidator<DepositRequest>
{
    public DepositRequestValidator() => RuleFor(x => x.Amount).MoneyAmount(max: 10_000).WithName("Valor");
}

public class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.To).NotEmpty().MaximumLength(200).WithName("Destinatário");
        RuleFor(x => x.Amount).MoneyAmount(max: 1_000_000).WithName("Valor");
    }
}

public class AccountService(
    IBankDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<DepositRequest> depositValidator,
    IValidator<TransferRequest> transferValidator)
{
    public async Task<AccountResponse> GetMineAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await (
                from a in db.Accounts
                join u in db.Users on a.UserId equals u.Id
                where a.UserId == userId
                select new AccountResponse(u.Name, u.Email, a.Number.ToString(), a.Balance))
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException("Conta não encontrada.");
    }

    public async Task<IReadOnlyList<TransactionResponse>> GetTransactionsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        // shortcut: fixed page of the latest 50, replaced by the filtered/paginated statement later in phase 1.
        return await db.Transactions
            .Where(t => db.Accounts.Any(a => a.Id == t.AccountId && a.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new TransactionResponse(t.Id, t.Type, t.Amount, t.Description, t.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task DepositAsync(DepositRequest request, CancellationToken ct)
    {
        await depositValidator.ValidateAndThrowAsync(request, ct);
        var accountId = await MyAccountIdAsync(ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).Single();
        account.Credit(request.Amount);
        db.Transactions.Add(Transaction.Record(account.Id, TransactionType.Deposit, request.Amount, "Depósito", null, Now()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task TransferAsync(TransferRequest request, CancellationToken ct)
    {
        await transferValidator.ValidateAndThrowAsync(request, ct);
        var userId = currentUser.UserId;
        var source = await (
                from a in db.Accounts
                join u in db.Users on a.UserId equals u.Id
                where a.UserId == userId
                select new { a.Id, u.Name })
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException("Conta não encontrada.");

        var to = request.To.Trim();
        var candidates = from a in db.Accounts join u in db.Users on a.UserId equals u.Id select new { a.Id, a.Number, u.Email, u.Name };
        var email = User.NormalizeEmail(to);
        candidates = long.TryParse(to, out var number)
            ? candidates.Where(c => c.Number == number)
            : candidates.Where(c => c.Email == email);
        var target = await candidates.SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Destinatário não encontrado.");
        if (target.Id == source.Id)
            throw new DomainException("transfer.same_account", "Não é possível transferir para a própria conta.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var locked = await db.LockAccountsAsync([source.Id, target.Id], ct);
        var from = locked.Single(a => a.Id == source.Id);
        var dest = locked.Single(a => a.Id == target.Id);

        from.Debit(request.Amount);
        dest.Credit(request.Amount);
        var now = Now();
        db.Transactions.AddRange(
            Transaction.Record(from.Id, TransactionType.TransferOut, -request.Amount, $"Transferência para {target.Name}", dest.Id, now),
            Transaction.Record(dest.Id, TransactionType.TransferIn, request.Amount, $"Transferência de {source.Name}", from.Id, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var id = await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
        return id ?? throw new UnauthorizedException("Conta não encontrada.");
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
