using Bank.Application.Auth;

namespace Bank.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");
        g.MapPost("/register", async (RegisterRequest req, AuthService auth, CancellationToken ct) =>
            TypedResults.Ok(await auth.RegisterAsync(req, ct)));
        g.MapPost("/login", async (LoginRequest req, AuthService auth, CancellationToken ct) =>
            TypedResults.Ok(await auth.LoginAsync(req, ct)));
    }
}
