using Bank.Api.Infrastructure;
using Bank.Application.Auth;

namespace Bank.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>The refresh token never reaches JavaScript: httpOnly cookie scoped to the auth routes.</summary>
    public const string RefreshCookie = "vela_refresh";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/register", async (RegisterRequest req, AuthService auth, HttpContext http, CancellationToken ct) =>
            TypedResults.Ok(Respond(http, await auth.RegisterAsync(req, ct))));
        g.MapPost("/login", async (LoginRequest req, AuthService auth, HttpContext http, CancellationToken ct) =>
            TypedResults.Ok(Respond(http, await auth.LoginAsync(req, ct))));
        g.MapPost("/login/2fa", async (TwoFactorLoginRequest req, AuthService auth, HttpContext http, CancellationToken ct) =>
            TypedResults.Ok(Respond(http, await auth.CompleteTwoFactorAsync(req, ct))));

        g.MapPost("/refresh", async (AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var session = await auth.RefreshAsync(http.Request.Cookies[RefreshCookie], ct);
                SetRefreshCookie(http, session);
                return TypedResults.Ok(session.Response);
            }
            catch
            {
                http.Response.Cookies.Delete(RefreshCookie, CookieOptions(http));
                throw;
            }
        });
        g.MapPost("/logout", async (AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
            http.Response.Cookies.Delete(RefreshCookie, CookieOptions(http));
            return TypedResults.NoContent();
        });
    }

    private static LoginResponse Respond(HttpContext http, LoginOutcome outcome)
    {
        if (outcome.Session is not null) SetRefreshCookie(http, outcome.Session);
        return outcome.Response;
    }

    private static void SetRefreshCookie(HttpContext http, IssuedSession session)
    {
        var options = CookieOptions(http);
        options.Expires = session.RefreshExpiresAt;
        http.Response.Cookies.Append(RefreshCookie, session.RefreshToken, options);
    }

    private static CookieOptions CookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        // Secure whenever the client connection is HTTPS (X-Forwarded-Proto behind the proxy); local HTTP dev still works.
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
    };
}
