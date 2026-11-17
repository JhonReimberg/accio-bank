using System.Security.Claims;
using Bank.Api.Auth;
using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/account").WithTags("Account").RequireAuthorization();
        g.MapGet("/", GetAccount);
        g.MapGet("/transactions", GetTransactions);
        g.MapPost("/deposits", Deposit);
        g.MapPost("/transfers", Transfer);
    }

    static async Task<IResult> GetAccount(ClaimsPrincipal user, BankDbContext db, CancellationToken ct)
    {
        var userId = user.UserId();
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new AccountResponse(a.User.Name, a.User.Email, a.Number.ToString(), a.Balance))
            .SingleOrDefaultAsync(ct);
        return account is null ? Results.Unauthorized() : Results.Ok(account);
    }

    static async Task<IResult> GetTransactions(ClaimsPrincipal user, BankDbContext db, CancellationToken ct)
    {
        var userId = user.UserId();
        // shortcut: fixed page of the latest 50, add cursor pagination if statements get long.
        var items = await db.Transactions.AsNoTracking()
            .Where(t => db.Accounts.Any(a => a.Id == t.AccountId && a.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new TransactionResponse(t.Id, t.Type, t.Amount, t.Description, t.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    static async Task<IResult> Deposit(DepositRequest req, ClaimsPrincipal user, BankDbContext db, CancellationToken ct)
    {
        if (!HasCents(req.Amount)) return InvalidCents();
        var userId = user.UserId();
        var accountId = await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
        if (accountId is null) return Results.Unauthorized();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Accounts.Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + req.Amount), ct);
        db.Transactions.Add(new Transaction
        {
            AccountId = accountId.Value,
            Type = TransactionType.Deposit,
            Amount = req.Amount,
            Description = "Depósito",
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    static async Task<IResult> Transfer(TransferRequest req, ClaimsPrincipal user, BankDbContext db, CancellationToken ct)
    {
        if (!HasCents(req.Amount)) return InvalidCents();
        var userId = user.UserId();
        var from = await db.Accounts.Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.User.Name }).SingleOrDefaultAsync(ct);
        if (from is null) return Results.Unauthorized();

        var to = req.To.Trim();
        var targets = long.TryParse(to, out var number)
            ? db.Accounts.Where(a => a.Number == number)
            : db.Accounts.Where(a => a.User.Email == AuthEndpoints.Normalize(to));
        var target = await targets.Select(a => new { a.Id, a.User.Name }).SingleOrDefaultAsync(ct);
        if (target is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Destinatário não encontrado.");
        if (target.Id == from.Id)
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Não é possível transferir para a própria conta.");

        var amount = req.Amount;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The debit only matches when there is enough balance, so two concurrent transfers can never overdraw.
        async Task<bool> Debit() => await db.Accounts
            .Where(a => a.Id == from.Id && a.Balance >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount), ct) == 1;
        Task Credit() => db.Accounts
            .Where(a => a.Id == target.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + amount), ct);

        // Lock the two rows in a fixed order (by id) so opposite transfers A->B and B->A cannot deadlock.
        bool debited;
        if (from.Id.CompareTo(target.Id) < 0)
        {
            debited = await Debit();
            if (debited) await Credit();
        }
        else
        {
            await Credit();
            debited = await Debit();
        }
        if (!debited)
        {
            await tx.RollbackAsync(ct);
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Saldo insuficiente.");
        }

        db.Transactions.AddRange(
            new Transaction
            {
                AccountId = from.Id,
                Type = TransactionType.TransferOut,
                Amount = -amount,
                Description = $"Transferência para {target.Name}",
                CounterpartyAccountId = target.Id,
            },
            new Transaction
            {
                AccountId = target.Id,
                Type = TransactionType.TransferIn,
                Amount = amount,
                Description = $"Transferência de {from.Name}",
                CounterpartyAccountId = from.Id,
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    static bool HasCents(decimal amount) => decimal.Round(amount, 2) == amount;

    static IResult InvalidCents() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["amount"] = ["Use no máximo 2 casas decimais."],
    });
}
