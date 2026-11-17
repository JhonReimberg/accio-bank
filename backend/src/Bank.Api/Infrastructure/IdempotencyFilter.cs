using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Infrastructure.Idempotency;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Bank.Api.Infrastructure;

/// <summary>
/// Requires an Idempotency-Key header and guarantees the same key never produces two operations:
/// a retry with the same key and body gets the original response back instead of running again.
/// </summary>
internal sealed class IdempotencyFilter(IdempotencyStore store, ICurrentUser currentUser, IOptions<JsonOptions> json)
    : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString().Trim();
        if (key.Length is 0 or > 100)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Envie o header {HeaderName} (até 100 caracteres) para esta operação.");

        var userId = currentUser.UserId;
        var (state, existing) = await store.BeginAsync(userId, key, await HashRequestAsync(http.Request), http.RequestAborted);
        switch (state)
        {
            case IdempotencyBegin.Mismatch:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Esta Idempotency-Key já foi usada em outra operação.");
            case IdempotencyBegin.InProgress:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Esta operação ainda está em processamento.");
            case IdempotencyBegin.Completed:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                return existing!.ResponseBody is null
                    ? Results.StatusCode(existing.StatusCode!.Value)
                    : Results.Text(existing.ResponseBody, "application/json", statusCode: existing.StatusCode);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await store.AbandonAsync(userId, key);
            throw;
        }

        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (status is >= 200 and < 300)
        {
            var body = result is IValueHttpResult { Value: { } value }
                ? JsonSerializer.Serialize(value, value.GetType(), json.Value.SerializerOptions)
                : null;
            await store.CompleteAsync(userId, key, status, body, CancellationToken.None);
        }
        else
        {
            await store.AbandonAsync(userId, key);
        }
        return result;
    }

    private static async Task<string> HashRequestAsync(HttpRequest request)
    {
        // The body was buffered by UseIdempotencyBuffering, so it can be read again after model binding.
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n{body}"));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>Marks an endpoint as requiring Idempotency-Key (used to document the header in OpenAPI).</summary>
internal sealed class IdempotentEndpoint;

internal static class IdempotencyExtensions
{
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<IdempotencyFilter>().WithMetadata(new IdempotentEndpoint());

    public static IApplicationBuilder UseIdempotencyBuffering(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName))
                http.Request.EnableBuffering();
            await next(http);
        });
}
