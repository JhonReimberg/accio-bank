using Bank.Api.Infrastructure;
using Bank.Application.Accounts;

namespace Bank.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/account").WithTags("Account").RequireAuthorization();
        g.MapGet("/", async (AccountService accounts, CancellationToken ct) => TypedResults.Ok(await accounts.GetMineAsync(ct)));
        g.MapGet("/transactions", async (AccountService accounts, CancellationToken ct) =>
            TypedResults.Ok(await accounts.GetTransactionsAsync(ct)));
        g.MapPost("/deposits", async (DepositRequest req, AccountService accounts, CancellationToken ct) =>
                TypedResults.Ok(await accounts.DepositAsync(req, ct)))
            .RequireIdempotency();
        g.MapPost("/transfers", async (TransferRequest req, AccountService accounts, CancellationToken ct) =>
                TypedResults.Ok(await accounts.TransferAsync(req, ct)))
            .RequireIdempotency();
    }
}
