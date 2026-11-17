using Bank.Api.Infrastructure;
using Bank.Application.Fraud;
using Bank.Domain.Fraud;

namespace Bank.Api.Endpoints;

public static class FraudEndpoints
{
    public static void MapFraudEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/fraud/reviews", async (FraudService fraud, CancellationToken ct) => TypedResults.Ok(await fraud.ListMineAsync(ct)))
            .WithTags("Fraud").RequireAuthorization();

        var g = app.MapGroup("/api/admin/fraud").WithTags("Back-office").RequireAuthorization(Policies.Backoffice).RequireRateLimiting(RateLimits.Sensitive);
        g.MapGet("/reviews", async (FraudService fraud, CancellationToken ct, FraudReviewStatus status = FraudReviewStatus.Pending, int page = 1) =>
            TypedResults.Ok(await fraud.ListAsync(status, page, ct)));
        g.MapPost("/reviews/{id:guid}/approve", async (Guid id, FraudService fraud, CancellationToken ct) =>
        {
            await fraud.ApproveAsync(id, ct);
            return TypedResults.NoContent();
        });
        g.MapPost("/reviews/{id:guid}/reject", async (Guid id, FraudService fraud, CancellationToken ct) =>
        {
            await fraud.RejectAsync(id, ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/policy", async (FraudService fraud, CancellationToken ct) => TypedResults.Ok(await fraud.PolicyAsync(ct)));
        g.MapPut("/policy", async (FraudPolicyRequest req, FraudService fraud, CancellationToken ct) => TypedResults.Ok(await fraud.UpdatePolicyAsync(req, ct)))
            .RequireAuthorization(Policies.Admin);
    }
}
