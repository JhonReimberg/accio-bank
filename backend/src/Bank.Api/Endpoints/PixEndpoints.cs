using Bank.Api.Infrastructure;
using Bank.Application.Pix;

namespace Bank.Api.Endpoints;

public static class PixEndpoints
{
    public static void MapPixEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pix").WithTags("PIX").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);

        // Keys
        g.MapGet("/keys", async (PixKeyService keys, CancellationToken ct) => TypedResults.Ok(await keys.ListAsync(ct)));
        g.MapPost("/keys", async (RegisterPixKeyRequest req, PixKeyService keys, CancellationToken ct) =>
            TypedResults.Ok(await keys.RegisterAsync(req, ct)));
        g.MapDelete("/keys/{id:guid}", async (Guid id, PixKeyService keys, CancellationToken ct) =>
        {
            await keys.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/keys/lookup", async (string key, PixService pix, CancellationToken ct) => TypedResults.Ok(await pix.LookupAsync(key, ct)));

        // Payments
        g.MapPost("/transfers", async (PixTransferRequest req, PixService pix, CancellationToken ct) =>
                TypedResults.Ok(await pix.SendAsync(req, ct)))
            .RequireIdempotency();
        g.MapGet("/transfers/by-transaction/{transactionId:guid}", async (Guid transactionId, PixService pix, CancellationToken ct) =>
            TypedResults.Ok(await pix.GetByTransactionAsync(transactionId, ct)));
        g.MapPost("/transfers/{id:guid}/refunds", async (Guid id, PixRefundRequest req, PixService pix, CancellationToken ct) =>
                TypedResults.Ok(await pix.RefundAsync(id, req, ct)))
            .RequireIdempotency();

        // Receive with QR Code / pay with "copia e cola"
        g.MapPost("/qrcodes", async (CreateQrCodeRequest req, PixService pix, CancellationToken ct) =>
            TypedResults.Ok(await pix.CreateQrCodeAsync(req, ct)));
        g.MapPost("/brcode/decode", async (DecodeBrCodeRequest req, PixService pix, CancellationToken ct) =>
            TypedResults.Ok(await pix.DecodeAsync(req, ct)));

        // Limits
        g.MapGet("/limits", async (PixService pix, CancellationToken ct) => TypedResults.Ok(await pix.GetLimitsAsync(ct)));
        g.MapPut("/limits", async (ChangePixLimitsRequest req, PixService pix, CancellationToken ct) =>
            TypedResults.Ok(await pix.ChangeLimitsAsync(req, ct)));

        // Scheduled and recurring
        g.MapGet("/schedules", async (PixScheduleService schedules, CancellationToken ct) => TypedResults.Ok(await schedules.ListAsync(ct)));
        g.MapPost("/schedules", async (CreatePixScheduleRequest req, PixScheduleService schedules, CancellationToken ct) =>
                TypedResults.Ok(await schedules.CreateAsync(req, ct)))
            .RequireIdempotency();
        g.MapDelete("/schedules/{id:guid}", async (Guid id, PixScheduleService schedules, CancellationToken ct) =>
        {
            await schedules.CancelAsync(id, ct);
            return TypedResults.NoContent();
        });

        // Favorite contacts
        g.MapGet("/contacts", async (PixKeyService keys, CancellationToken ct) => TypedResults.Ok(await keys.ListContactsAsync(ct)));
        g.MapPost("/contacts", async (AddContactRequest req, PixKeyService keys, CancellationToken ct) =>
            TypedResults.Ok(await keys.AddContactAsync(req, ct)));
        g.MapDelete("/contacts/{id:guid}", async (Guid id, PixKeyService keys, CancellationToken ct) =>
        {
            await keys.DeleteContactAsync(id, ct);
            return TypedResults.NoContent();
        });
    }
}
