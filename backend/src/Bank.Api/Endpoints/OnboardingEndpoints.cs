using Bank.Api.Infrastructure;
using Bank.Application.Onboarding;
using Bank.Domain.Customers;

namespace Bank.Api.Endpoints;

public static class OnboardingEndpoints
{
    public static void MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/onboarding").WithTags("Onboarding (KYC)").RequireAuthorization();

        g.MapGet("/", async (OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.GetStatusAsync(ct)));
        g.MapPut("/personal-data", async (PersonalDataRequest req, OnboardingService onboarding, CancellationToken ct) =>
        {
            await onboarding.SavePersonalDataAsync(req, ct);
            return TypedResults.NoContent();
        });
        g.MapGet("/cep/{cep}", async (string cep, OnboardingService onboarding, CancellationToken ct) =>
            TypedResults.Ok(await onboarding.LookupCepAsync(cep, ct)));
        g.MapPut("/address", async (AddressRequest req, OnboardingService onboarding, CancellationToken ct) =>
        {
            await onboarding.SaveAddressAsync(req, ct);
            return TypedResults.NoContent();
        });
        g.MapPut("/financial-profile", async (FinancialProfileRequest req, OnboardingService onboarding, CancellationToken ct) =>
        {
            await onboarding.SaveFinancialProfileAsync(req, ct);
            return TypedResults.NoContent();
        });
        g.MapPost("/documents/{type}", async (DocumentType type, IFormFile file, OnboardingService onboarding, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                await onboarding.UploadDocumentAsync(type, stream, file.Length, ct);
                return TypedResults.NoContent();
            })
            // Bearer-token API (no auth cookie on this route), so there is no CSRF vector to protect.
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimits.Sensitive);
        g.MapPost("/submit", async (OnboardingService onboarding, CancellationToken ct) =>
            TypedResults.Ok(await onboarding.SubmitAsync(ct)));
    }
}
