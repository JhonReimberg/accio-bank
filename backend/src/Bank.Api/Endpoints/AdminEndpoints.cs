using Bank.Api.Infrastructure;
using Bank.Application.Abstractions;
using Bank.Application.Admin;
using Bank.Application.Onboarding;
using Bank.Domain.Credit;
using Bank.Domain.Customers;

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

        g.MapPost("/accounts/{accountId:guid}/block", async (Guid accountId, AccountStatusRequest req, AdminService admin, CancellationToken ct) =>
        {
            await admin.BlockAccountAsync(accountId, req, ct);
            return TypedResults.NoContent();
        });
        g.MapPost("/accounts/{accountId:guid}/unblock", async (Guid accountId, AdminService admin, CancellationToken ct) =>
        {
            await admin.UnblockAccountAsync(accountId, ct);
            return TypedResults.NoContent();
        });

        g.MapGet("/dashboard", async (BackofficeService backoffice, CancellationToken ct) => TypedResults.Ok(await backoffice.DashboardAsync(ct)));
        g.MapGet("/customers", async (BackofficeService backoffice, CancellationToken ct, string? search, int page = 1, int pageSize = 20) =>
            TypedResults.Ok(await backoffice.SearchCustomersAsync(search, page, pageSize, ct)));
        g.MapGet("/customers/{userId:guid}", async (Guid userId, BackofficeService backoffice, CancellationToken ct) =>
            TypedResults.Ok(await backoffice.GetCustomerAsync(userId, ct)));
        g.MapPut("/accounts/{accountId:guid}/overdraft-limit", async (Guid accountId, SetOverdraftLimitRequest req, BackofficeService backoffice, CancellationToken ct) =>
        {
            await backoffice.SetOverdraftLimitAsync(accountId, req, ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/credit/policy", async (BackofficeService backoffice, CancellationToken ct) => TypedResults.Ok(await backoffice.GetPolicyAsync(ct)));
        g.MapPut("/credit/policy", async (CreditPolicyValues values, BackofficeService backoffice, CancellationToken ct) =>
                TypedResults.Ok(await backoffice.UpdatePolicyAsync(values, ct)))
            .RequireAuthorization(Policies.Admin);

        var kyc = g.MapGroup("/kyc");
        kyc.MapGet("/", async (KycReviewService review, CancellationToken ct, KycStatus status = KycStatus.PendingReview, int page = 1, int pageSize = 20) =>
            TypedResults.Ok(await review.ListAsync(status, page, pageSize, ct)));
        kyc.MapGet("/{customerId:guid}", async (Guid customerId, KycReviewService review, CancellationToken ct) =>
            TypedResults.Ok(await review.GetAsync(customerId, ct)));
        kyc.MapGet("/{customerId:guid}/documents/{documentId:guid}", async (Guid customerId, Guid documentId, KycReviewService review, CancellationToken ct) =>
        {
            var file = await review.OpenDocumentAsync(customerId, documentId, ct);
            return TypedResults.File(file.Content, file.ContentType, file.FileName);
        });
        kyc.MapPost("/{customerId:guid}/approve", async (Guid customerId, KycReviewService review, ICurrentUser user, CancellationToken ct) =>
        {
            await review.ApproveAsync(customerId, user.UserId, ct);
            return TypedResults.NoContent();
        });
        kyc.MapPost("/{customerId:guid}/reject", async (Guid customerId, RejectKycRequest req, KycReviewService review, ICurrentUser user, CancellationToken ct) =>
        {
            await review.RejectAsync(customerId, user.UserId, req, ct);
            return TypedResults.NoContent();
        });
    }
}
