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

        g.MapGet("/photo", async (ProfilePhotoService photos, CancellationToken ct) =>
        {
            var file = await photos.OpenAsync(ct);
            return TypedResults.File(file.Content, file.ContentType);
        });
        g.MapPut("/photo", async (IFormFile file, ProfilePhotoService photos, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                await photos.SaveAsync(stream, file.Length, ct);
                return TypedResults.NoContent();
            })
            // Bearer-token API (no auth cookie on this route), so there is no CSRF vector to protect.
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimits.Sensitive);
        g.MapDelete("/photo", async (ProfilePhotoService photos, CancellationToken ct) =>
        {
            await photos.RemoveAsync(ct);
            return TypedResults.NoContent();
        });

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
