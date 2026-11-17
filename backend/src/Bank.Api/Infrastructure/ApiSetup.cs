using System.Threading.RateLimiting;
using Bank.Application.Abstractions;
using Bank.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace Bank.Api.Infrastructure;

public static class Policies
{
    public const string Backoffice = "backoffice";
    public const string Admin = "admin";
}

public static class RateLimits
{
    /// <summary>Login, registration and other anonymous credential endpoints, per IP.</summary>
    public const string Auth = "auth";
    /// <summary>Money movements and back-office actions, per user.</summary>
    public const string Sensitive = "sensitive";
}

public record RateLimitOptions(int AuthPermitLimit = 10, int SensitivePermitLimit = 30, int WindowSeconds = 60);

internal static class ApiSetup
{
    public static IServiceCollection AddApiAuth(this IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new()
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Issuer,
                    IssuerSigningKey = jwt.SigningKey,
                    RoleClaimType = ClaimNames.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                // WebSockets cannot send headers: the hub receives the access token in the query string.
                o.Events = new()
                {
                    OnMessageReceived = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api/hubs") && ctx.Request.Query["access_token"] is { Count: > 0 } token)
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Backoffice, p => p.RequireRole("Analyst", "Admin"))
            .AddPolicy(Policies.Admin, p => p.RequireRole("Admin"));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, RateLimitOptions limits)
    {
        services.AddRateLimiter(o =>
        {
            var window = TimeSpan.FromSeconds(limits.WindowSeconds);
            o.AddPolicy(RateLimits.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPermitLimit, Window = window }));
            o.AddPolicy(RateLimits.Sensitive, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.SensitivePermitLimit, Window = window }));
            o.OnRejected = async (ctx, ct) =>
            {
                var http = ctx.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = limits.WindowSeconds.ToString();
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Muitas tentativas. Aguarde um pouco e tente novamente.",
                    },
                });
            };
        });
        return services;
    }

    /// <summary>Behind nginx the client IP arrives in X-Forwarded-For. Only enable when a trusted proxy is in front.</summary>
    public static IServiceCollection AddTrustedProxy(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

    public static IServiceCollection AddApiDocs(this IServiceCollection services) =>
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "Vela — banco digital (API)";
                doc.Info.Description = "Projeto fictício de portfólio. Autentique em /api/auth/login e use o token como Bearer.";
                doc.Components ??= new OpenApiComponents();
                doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                };
                doc.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] }];
                return Task.CompletedTask;
            });
            o.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentEndpoint>().Any())
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = IdempotencyFilter.HeaderName,
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Chave única por operação (ex.: um UUID). Repetir a chave devolve a resposta original sem executar de novo.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    });
                }
                return Task.CompletedTask;
            });
        });
}
