using Bank.Api.Infrastructure;
using Bank.Application.Accounts;

namespace Bank.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/account").WithTags("Account").RequireAuthorization();
        g.MapGet("/", async (AccountService accounts, CancellationToken ct) => TypedResults.Ok(await accounts.GetMineAsync(ct)));

        g.MapGet("/statement", async ([AsParameters] StatementQuery query, StatementService statement, CancellationToken ct) =>
            TypedResults.Ok(await statement.GetAsync(query, ct)));
        g.MapGet("/statement/{transactionId:guid}/receipt", async (Guid transactionId, StatementService statement, CancellationToken ct) =>
            TypedResults.File(await statement.GetReceiptPdfAsync(transactionId, ct), "application/pdf", $"comprovante-{transactionId}.pdf"));

        g.MapPost("/deposits", async (DepositRequest req, AccountService accounts, CancellationToken ct) =>
                TypedResults.Ok(await accounts.DepositAsync(req, ct)))
            .RequireIdempotency()
            .RequireRateLimiting(RateLimits.Sensitive);
        g.MapPost("/transfers", async (TransferRequest req, AccountService accounts, CancellationToken ct) =>
                TypedResults.Ok(await accounts.TransferAsync(req, ct)))
            .RequireIdempotency()
            .RequireRateLimiting(RateLimits.Sensitive);
    }
}
