using Bank.Api.Infrastructure;
using Bank.Application.Auth;

namespace Bank.Api.Endpoints;

public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/security").WithTags("Security")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Sensitive);

        g.MapGet("/", async (SecurityService security, CancellationToken ct) => TypedResults.Ok(await security.GetOverviewAsync(ct)));

        g.MapPost("/2fa/setup", async (SecurityService security, CancellationToken ct) =>
            TypedResults.Ok(await security.BeginTwoFactorSetupAsync(ct)));
        g.MapPost("/2fa/enable", async (TwoFactorCodeRequest req, SecurityService security, CancellationToken ct) =>
        {
            await security.EnableTwoFactorAsync(req, ct);
            return TypedResults.NoContent();
        });
        g.MapPost("/2fa/disable", async (TwoFactorCodeRequest req, SecurityService security, CancellationToken ct) =>
        {
            await security.DisableTwoFactorAsync(req, ct);
            return TypedResults.NoContent();
        });

        g.MapPut("/pin", async (SetPinRequest req, SecurityService security, CancellationToken ct) =>
        {
            await security.SetTransactionPinAsync(req, ct);
            return TypedResults.NoContent();
        });

        g.MapGet("/devices", async (SecurityService security, CancellationToken ct) => TypedResults.Ok(await security.ListDevicesAsync(ct)));
        g.MapPost("/devices/{id:guid}/trust", async (Guid id, SecurityService security, CancellationToken ct) =>
        {
            await security.TrustDeviceAsync(id, ct);
            return TypedResults.NoContent();
        });
        g.MapDelete("/devices/{id:guid}", async (Guid id, SecurityService security, CancellationToken ct) =>
        {
            await security.RemoveDeviceAsync(id, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/sessions/revoke-all", async (SecurityService security, CancellationToken ct) =>
        {
            await security.RevokeAllSessionsAsync(ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/access-history", async (SecurityService security, CancellationToken ct, int page = 1, int pageSize = 20) =>
            TypedResults.Ok(await security.AccessHistoryAsync(page, pageSize, ct)));
    }
}
