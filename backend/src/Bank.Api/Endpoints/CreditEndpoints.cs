using Bank.Api.Infrastructure;
using Bank.Application.Abstractions;
using Bank.Application.Credit;

namespace Bank.Api.Endpoints;

public static class CreditEndpoints
{
    public static void MapCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/credit").WithTags("Credit").RequireAuthorization().RequireRateLimiting(RateLimits.Sensitive);

        g.MapGet("/score", async (ScoreService scores, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(await scores.GetAsync(user.UserId, ct)));
        g.MapGet("/eligibility", async (CreditService credit, CancellationToken ct) => TypedResults.Ok(await credit.GetEligibilityAsync(ct)));
        g.MapPost("/loans/simulate", async (SimulateLoanRequest req, CreditService credit, CancellationToken ct) =>
            TypedResults.Ok(await credit.SimulateAsync(req, ct)));
        g.MapPost("/loans", async (ContractLoanRequest req, CreditService credit, CancellationToken ct) =>
                TypedResults.Ok(await credit.ContractAsync(req, ct)))
            .RequireIdempotency();
        g.MapGet("/loans", async (CreditService credit, CancellationToken ct) => TypedResults.Ok(await credit.ListLoansAsync(ct)));
        g.MapPost("/loans/{loanId:guid}/installments/{number:int}/pay",
                async (Guid loanId, int number, PayInstallmentRequest req, CreditService credit, CancellationToken ct) =>
                    TypedResults.Ok(await credit.PayInstallmentAsync(loanId, number, req, ct)))
            .RequireIdempotency();
        g.MapGet("/loans/{loanId:guid}/contract", async (Guid loanId, CreditService credit, CancellationToken ct) =>
            TypedResults.File(await credit.GetContractPdfAsync(loanId, ct), "application/pdf", $"contrato-{loanId}.pdf"));

        g.MapGet("/overdraft", async (CreditService credit, CancellationToken ct) => TypedResults.Ok(await credit.GetOverdraftAsync(ct)));
        g.MapPut("/overdraft", async (SetOverdraftRequest req, CreditService credit, CancellationToken ct) =>
            TypedResults.Ok(await credit.SetOverdraftAsync(req, ct)));
    }
}
