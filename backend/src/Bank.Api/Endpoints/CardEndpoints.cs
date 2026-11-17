using Bank.Api.Infrastructure;
using Bank.Application.Cards;

namespace Bank.Api.Endpoints;

public static class CardEndpoints
{
    public record RevealRequest(string Pin);

    public static void MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cards").WithTags("Cards").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);

        g.MapGet("/", async (CardService cards, CancellationToken ct) => TypedResults.Ok(await cards.ListAsync(ct)));
        g.MapPost("/", async (IssueCardRequest req, CardService cards, CancellationToken ct) => TypedResults.Ok(await cards.IssueAsync(req, ct)));
        g.MapPost("/{id:guid}/reveal", async (Guid id, RevealRequest req, CardService cards, CancellationToken ct) =>
            TypedResults.Ok(await cards.RevealAsync(id, req.Pin, ct)));
        g.MapPatch("/{id:guid}", async (Guid id, CardSettingsRequest req, CardService cards, CancellationToken ct) =>
            TypedResults.Ok(await cards.UpdateSettingsAsync(id, req, ct)));
        // Purchase simulator: stands in for the card network sending an authorization request.
        g.MapPost("/{id:guid}/purchases", async (Guid id, PurchaseRequest req, CardService cards, CancellationToken ct) =>
                TypedResults.Ok(await cards.PurchaseAsync(id, req, ct)))
            .RequireIdempotency();

        g.MapGet("/credit-line", async (CardService cards, CancellationToken ct) =>
            await cards.GetCreditLineAsync(ct) is { } line ? Results.Ok(line) : Results.NoContent());
        g.MapGet("/credit-line/offer", async (CardService cards, CancellationToken ct) => TypedResults.Ok(new { maxLimit = await cards.MaxLimitAsync(ct) }));
        g.MapPost("/credit-line", async (OpenCreditLineRequest req, CardService cards, CancellationToken ct) =>
            TypedResults.Ok(await cards.OpenCreditLineAsync(req, ct)));
        g.MapPut("/credit-line/limit", async (ChangeLimitRequest req, CardService cards, CancellationToken ct) =>
            TypedResults.Ok(await cards.ChangeLimitAsync(req, ct)));
        g.MapPost("/invoices/{invoiceId:guid}/payments", async (Guid invoiceId, PayInvoiceRequest req, CardService cards, CancellationToken ct) =>
                TypedResults.Ok(await cards.PayInvoiceAsync(invoiceId, req, ct)))
            .RequireIdempotency();
    }
}
