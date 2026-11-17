using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Ledger;
using Bank.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Accounts;

public record AccountResponse(
    string Name, string Email, string Number, decimal AvailableBalance, decimal BlockedBalance, decimal TotalBalance);

public record TransactionResponse(
    Guid Id, TransactionKind Kind, decimal Amount, string Description, decimal BalanceAfter, DateTime CreatedAt);

public record DepositRequest(decimal Amount);

/// <param name="To">Account number or the recipient's e-mail.</param>
public record TransferRequest(string To, decimal Amount);

public record OperationResponse(Guid TransactionId);

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
    LedgerPosting ledger,
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
                select new AccountResponse(
                    u.Name, u.Email, a.Number.ToString(), a.Balance - a.BlockedBalance, a.BlockedBalance, a.Balance))
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException("Conta não encontrada.");
    }

    public async Task<IReadOnlyList<TransactionResponse>> GetTransactionsAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        // shortcut: fixed page of the latest 50, replaced by the filtered/paginated statement later in phase 1.
        return await (
                from e in db.LedgerEntries
                join t in db.LedgerTransactions on e.TransactionId equals t.Id
                where e.AccountId == accountId
                orderby e.CreatedAt descending
                select new TransactionResponse(
                    t.Id, t.Kind, e.Direction == EntryDirection.Credit ? e.Amount : -e.Amount, e.Description, e.BalanceAfter, e.CreatedAt))
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<OperationResponse> DepositAsync(DepositRequest request, CancellationToken ct)
    {
        await depositValidator.ValidateAndThrowAsync(request, ct);
        var accountId = await MyAccountIdAsync(ct);

        // shortcut: every deposit locks the single cash account row; shard it if cash-in becomes a hot path.
        var tx = await ledger.ExecuteAsync([SystemAccounts.Cash, accountId], (accounts, now) =>
            LedgerTransaction.Transfer(
                TransactionKind.Deposit, accounts[SystemAccounts.Cash], accounts[accountId], request.Amount,
                "Depósito de cliente", "Depósito", now), ct);
        return new OperationResponse(tx.Id);
    }

    public async Task<OperationResponse> TransferAsync(TransferRequest request, CancellationToken ct)
    {
        await transferValidator.ValidateAndThrowAsync(request, ct);
        var userId = currentUser.UserId;
        var source = await CustomerAccounts().Where(c => c.UserId == userId).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("Conta não encontrada.");

        var to = request.To.Trim();
        var email = User.NormalizeEmail(to);
        var target = await (long.TryParse(to, out var number)
                ? CustomerAccounts().Where(c => c.Number == number)
                : CustomerAccounts().Where(c => c.Email == email))
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Destinatário não encontrado.");

        var tx = await ledger.ExecuteAsync([source.Id, target.Id], (accounts, now) =>
            LedgerTransaction.Transfer(
                TransactionKind.InternalTransfer, accounts[source.Id], accounts[target.Id], request.Amount,
                $"Transferência para {target.Name}", $"Transferência de {source.Name}", now), ct);
        return new OperationResponse(tx.Id);
    }

    // Member-init (not constructor) projection, so EF can translate further Where clauses on it.
    private sealed class CustomerAccount
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public long Number { get; init; }
        public string Email { get; init; } = "";
        public string Name { get; init; } = "";
    }

    private IQueryable<CustomerAccount> CustomerAccounts() =>
        from a in db.Accounts
        join u in db.Users on a.UserId equals u.Id
        where a.Type == AccountType.Customer
        select new CustomerAccount { Id = a.Id, UserId = u.Id, Number = a.Number, Email = u.Email, Name = u.Name };

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var id = await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
        return id ?? throw new UnauthorizedException("Conta não encontrada.");
    }
}
