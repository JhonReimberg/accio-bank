using Bank.Api.Infrastructure;
using Bank.Application.Investments;

namespace Bank.Api.Endpoints;

public static class InvestmentEndpoints
{
    public static void MapInvestmentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/investments").WithTags("Investments").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);

        g.MapGet("/", async (InvestmentService investments, CancellationToken ct) => TypedResults.Ok(await investments.ListAsync(ct)));
        g.MapPost("/", async (OpenInvestmentRequest req, InvestmentService investments, CancellationToken ct) =>
            TypedResults.Ok(await investments.OpenAsync(req, ct)));
        g.MapPost("/{id:guid}/applications", async (Guid id, MoveInvestmentRequest req, InvestmentService investments, CancellationToken ct) =>
                TypedResults.Ok(await investments.ApplyAsync(id, req, ct)))
            .RequireIdempotency();
        g.MapGet("/{id:guid}/redemptions/preview", async (Guid id, decimal amount, InvestmentService investments, CancellationToken ct) =>
            TypedResults.Ok(await investments.PreviewRedemptionAsync(id, amount, ct)));
        g.MapPost("/{id:guid}/redemptions", async (Guid id, MoveInvestmentRequest req, InvestmentService investments, CancellationToken ct) =>
                TypedResults.Ok(await investments.RedeemAsync(id, req, ct)))
            .RequireIdempotency();
        g.MapPut("/{id:guid}/round-up", async (Guid id, RoundUpRequest req, InvestmentService investments, CancellationToken ct) =>
            TypedResults.Ok(await investments.SetRoundUpAsync(id, req, ct)));

        app.MapPost("/api/credit/guaranteed-loans", async (ContractGuaranteedLoanRequest req, GuaranteedLoanService loans, CancellationToken ct) =>
                TypedResults.Ok(await loans.ContractAsync(req, ct)))
            .WithTags("Credit").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive).RequireIdempotency();
    }
}
