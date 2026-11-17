using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Auth;

public record RegisterRequest(string Name, string Email, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, string Name);

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100).WithName("Nome");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200).WithName("E-mail");
        RuleFor(x => x.Password).NotEmpty().Length(8, 100).WithName("Senha");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithName("E-mail");
        RuleFor(x => x.Password).NotEmpty().WithName("Senha");
    }
}

public class AuthService(
    IBankDbContext db,
    ISecretHasher hasher,
    ITokenService tokens,
    IAuditLog audit,
    TimeProvider clock,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);
        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("E-mail já cadastrado.");

        var now = clock.GetUtcNow().UtcDateTime;
        var user = User.Create(request.Name, request.Email, now);
        user.SetPasswordHash(hasher.Hash(request.Password));
        db.Users.Add(user);
        db.Accounts.Add(Account.OpenCustomer(user.Id, now));
        audit.Record("auth.registered", nameof(User), user.Id, after: new { user.Email, user.Role }, actorUserId: user.Id);
        await db.SaveChangesAsync(ct);
        return new AuthResponse(tokens.CreateAccessToken(user), user.Name);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !hasher.Verify(user.PasswordHash, request.Password))
        {
            audit.Record("auth.login_failed", nameof(User), user?.Id, after: new { Email = email }, actorUserId: user?.Id);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("E-mail ou senha inválidos.");
        }
        audit.Record("auth.login_succeeded", nameof(User), user.Id, actorUserId: user.Id);
        await db.SaveChangesAsync(ct);
        return new AuthResponse(tokens.CreateAccessToken(user), user.Name);
    }
}
