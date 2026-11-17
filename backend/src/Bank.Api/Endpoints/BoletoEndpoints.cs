using Bank.Api.Infrastructure;
using Bank.Application.Boletos;

namespace Bank.Api.Endpoints;

public static class BoletoEndpoints
{
    public static void MapBoletoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/boletos").WithTags("Boletos").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);
        g.MapGet("/", async (BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.ListIssuedAsync(ct)));
        g.MapPost("/", async (IssueBoletoRequest req, BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.IssueAsync(req, ct)));
        g.MapDelete("/{id:guid}", async (Guid id, BoletoService boletos, CancellationToken ct) =>
        {
            await boletos.CancelIssuedAsync(id, ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/inspect", async (string code, BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.InspectAsync(code, ct)));
        g.MapGet("/payments", async (BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.ListPaymentsAsync(ct)));
        g.MapPost("/payments", async (PayBoletoRequest req, BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.PayAsync(req, ct)))
            .RequireIdempotency();
        g.MapDelete("/payments/{id:guid}", async (Guid id, BoletoService boletos, CancellationToken ct) =>
        {
            await boletos.CancelScheduledAsync(id, ct);
            return TypedResults.NoContent();
        });

        var r = app.MapGroup("/api/recharges").WithTags("Recharges").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);
        r.MapGet("/options", () => TypedResults.Ok(new { operators = BoletoService.Operators, amounts = BoletoService.RechargeAmounts }));
        r.MapPost("/", async (RechargeRequest req, BoletoService boletos, CancellationToken ct) => TypedResults.Ok(await boletos.RechargeAsync(req, ct)))
            .RequireIdempotency();
    }
}
