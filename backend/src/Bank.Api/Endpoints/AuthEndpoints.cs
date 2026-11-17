using Bank.Api.Auth;
using Bank.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");
        g.MapPost("/register", Register);
        g.MapPost("/login", Login);
    }

    static async Task<IResult> Register(RegisterRequest req, BankDbContext db, IPasswordHasher<User> hasher, TokenService tokens, CancellationToken ct)
    {
        var user = new User { Name = req.Name.Trim(), Email = Normalize(req.Email), Account = new Account() };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "E-mail já cadastrado.");
        }
        return Results.Ok(new AuthResponse(tokens.Create(user), user.Name));
    }

    static async Task<IResult> Login(LoginRequest req, BankDbContext db, IPasswordHasher<User> hasher, TokenService tokens, CancellationToken ct)
    {
        var email = Normalize(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha inválidos.");
        return Results.Ok(new AuthResponse(tokens.Create(user), user.Name));
    }

    internal static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
