using Bank.Api.Infrastructure;
using Bank.Application.Admin;

namespace Bank.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").WithTags("Back-office")
            .RequireAuthorization(Policies.Backoffice)
            .RequireRateLimiting(RateLimits.Sensitive);

        g.MapPost("/accounts/{accountId:guid}/holds", async (Guid accountId, PlaceHoldRequest req, AdminService admin, CancellationToken ct) =>
            TypedResults.Ok(await admin.PlaceHoldAsync(accountId, req, ct)));
        g.MapPost("/holds/{holdId:guid}/release", async (Guid holdId, AdminService admin, CancellationToken ct) =>
            TypedResults.Ok(await admin.ReleaseHoldAsync(holdId, ct)));
        g.MapGet("/audit-logs", async ([AsParameters] AuditLogQuery query, AdminService admin, CancellationToken ct) =>
            TypedResults.Ok(await admin.SearchAuditLogsAsync(query, ct)));
        g.MapGet("/ledger/reconciliation", async (AdminService admin, CancellationToken ct) =>
                TypedResults.Ok(await admin.ReconcileAsync(ct)))
            .RequireAuthorization(Policies.Admin);
    }
}
