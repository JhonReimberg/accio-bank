using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Auth;

public record RegisterRequest(string Name, string Email, string Password);

public record LoginRequest(string Email, string Password);

public record TwoFactorLoginRequest(string ChallengeToken, string Code, bool TrustDevice = false);

public record SessionResponse(string AccessToken, DateTime ExpiresAt, string Name, Role Role);

/// <summary>Either a session, or a challenge to be completed with the 2FA code.</summary>
public record LoginResponse(bool RequiresTwoFactor, string? ChallengeToken, SessionResponse? Session);

/// <summary>A session plus the refresh token, which the API puts in an httpOnly cookie (never in the body).</summary>
public record IssuedSession(SessionResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

public record LoginOutcome(LoginResponse Response, IssuedSession? Session);

public record SessionOptions(int RefreshTokenDays = 14);

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
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200).WithName("E-mail");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100).WithName("Senha");
    }
}

public class TwoFactorLoginRequestValidator : AbstractValidator<TwoFactorLoginRequest>
{
    public TwoFactorLoginRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Informe o código de 6 dígitos do aplicativo autenticador.");
    }
}

public class AuthService(
    IBankDbContext db,
    ISecretHasher hasher,
    ITokenService tokens,
    ITotpService totp,
    ISecretProtector protector,
    ICurrentUser request,
    IAuditLog audit,
    SessionOptions sessionOptions,
    TimeProvider clock,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<TwoFactorLoginRequest> twoFactorValidator)
{
    private const string InvalidCredentials = "E-mail ou senha inválidos.";

    public async Task<LoginOutcome> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(req, ct);
        var email = User.NormalizeEmail(req.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("E-mail já cadastrado.");

        var now = Now();
        var user = User.Create(req.Name, req.Email, now);
        user.SetPasswordHash(hasher.Hash(req.Password));
        db.Users.Add(user);
        db.Accounts.Add(Account.OpenCustomer(user.Id, now));
        var device = Device.Register(user.Id, DeviceKey(), DeviceNames.FromUserAgent(request.UserAgent), request.IpAddress, now, alertOwner: false);
        db.Devices.Add(device);
        audit.Record("auth.registered", nameof(User), user.Id, after: new { user.Email, user.Role }, actorUserId: user.Id);
        return await CompleteSignInAsync(user, device, ct);
    }

    public async Task<LoginOutcome> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(req, ct);
        var email = User.NormalizeEmail(req.Email);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        if (userId is null)
        {
            // Same hashing cost as a real check, so response time does not reveal which e-mails exist.
            _dummyPasswordHash ??= hasher.Hash(Guid.NewGuid().ToString());
            hasher.Verify(_dummyPasswordHash, req.Password);
            var failure = Fail(null, email, "unknown_email");
            await db.SaveChangesAsync(ct);
            throw failure;
        }

        return await WithLockedUserAsync(userId.Value, async user =>
        {
            var now = Now();
            try
            {
                user.EnsureCanSignIn(now);
            }
            catch (DomainException)
            {
                RecordAttempt(user.Id, email, succeeded: false, "locked_out");
                throw;
            }
            if (!hasher.Verify(user.PasswordHash, req.Password))
            {
                user.RegisterFailedSignIn(now);
                throw Fail(user.Id, email, "wrong_password");
            }

            var device = await ResolveDeviceAsync(user.Id, now, ct);
            if (user.TwoFactorEnabled && !device.Trusted)
                return new LoginOutcome(new LoginResponse(true, tokens.CreateChallengeToken(user.Id, device.DeviceKey), null), null);
            return await CompleteSignInAsync(user, device, ct);
        }, ct);
    }

    public async Task<LoginOutcome> CompleteTwoFactorAsync(TwoFactorLoginRequest req, CancellationToken ct)
    {
        await twoFactorValidator.ValidateAndThrowAsync(req, ct);
        var challenge = await tokens.ReadChallengeTokenAsync(req.ChallengeToken)
            ?? throw new UnauthorizedException("A verificação expirou. Entre novamente.");

        return await WithLockedUserAsync(challenge.UserId, async user =>
        {
            var now = Now();
            user.EnsureCanSignIn(now);
            // shortcut: a valid code can be replayed within its 30s window; store the last used time step to block that.
            if (!user.TwoFactorEnabled || !totp.Verify(protector.Unprotect(user.TwoFactorSecret!), req.Code, now))
            {
                user.RegisterFailedSignIn(now);
                RecordAttempt(user.Id, user.Email, succeeded: false, "wrong_2fa_code");
                throw new UnauthorizedException("Código de verificação inválido.");
            }

            var device = await db.Devices.SingleAsync(d => d.UserId == user.Id && d.DeviceKey == challenge.DeviceKey, ct);
            if (req.TrustDevice)
            {
                device.Trust();
                audit.Record("security.device_trusted", nameof(Device), device.Id, after: new { device.Name }, actorUserId: user.Id);
            }
            return await CompleteSignInAsync(user, device, ct);
        }, ct);
    }

    /// <summary>
    /// Runs a sign-in step with the user row locked. Expected failures (wrong password, lockout) are
    /// committed before being rethrown, so attempt counters survive and cannot be raced.
    /// </summary>
    private async Task<T> WithLockedUserAsync<T>(Guid userId, Func<User, Task<T>> step, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var user = await db.LockUserAsync(userId, ct) ?? throw new UnauthorizedException(InvalidCredentials);
        Exception? expected = null;
        T result = default!;
        try
        {
            result = await step(user);
        }
        catch (Exception e) when (e is DomainException or UnauthorizedException)
        {
            expected = e;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (expected is not null) ExceptionDispatchInfo.Throw(expected);
        return result;
    }

    // A real PBKDF2 hash (computed once), so verifying against it costs the same as a real user.
    private static string? _dummyPasswordHash;

    /// <summary>
    /// Exchanges a refresh token for a new session and rotates it. Presenting a token that was already
    /// rotated means it leaked: the whole token family is revoked and the user must sign in again.
    /// </summary>
    public async Task<IssuedSession> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new UnauthorizedException("Sessão expirada. Entre novamente.");
        var hash = tokens.HashRefreshToken(refreshToken);
        var now = Now();
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Sessão expirada. Entre novamente.");

        if (current.RevokedReason == RevokeReasons.Rotated)
        {
            await db.RefreshTokens.Where(t => t.FamilyId == current.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokeReasons.ReuseDetected), ct);
            audit.Record("auth.refresh_token_reuse_detected", nameof(RefreshToken), current.FamilyId, actorUserId: current.UserId);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Sessão encerrada por segurança. Entre novamente.");
        }
        if (!current.IsActive(now)) throw new UnauthorizedException("Sessão expirada. Entre novamente.");

        var user = await db.Users.SingleAsync(u => u.Id == current.UserId, ct);
        var (next, raw) = NewRefreshToken(user.Id, current.FamilyId, current.DeviceId, now);
        // Atomic claim: if two refreshes race with the same token, only one wins the rotation.
        var claimed = await db.RefreshTokens.Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, RevokeReasons.Rotated)
                .SetProperty(t => t.ReplacedById, next.Id), ct);
        if (claimed == 0) throw new UnauthorizedException("Sessão atualizada em outra aba. Tente novamente.");

        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        return Issue(user, raw, next);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = tokens.HashRefreshToken(refreshToken);
        var now = Now();
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokeReasons.Logout), ct);
    }

    private async Task<LoginOutcome> CompleteSignInAsync(User user, Device device, CancellationToken ct)
    {
        var now = Now();
        user.RegisterSuccessfulSignIn();
        RecordAttempt(user.Id, user.Email, succeeded: true, null);
        audit.Record("auth.signed_in", nameof(User), user.Id, after: new { Device = device.Name }, actorUserId: user.Id);
        var (refresh, raw) = NewRefreshToken(user.Id, familyId: Guid.CreateVersion7(), device.Id, now);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        var session = Issue(user, raw, refresh);
        return new LoginOutcome(new LoginResponse(false, null, session.Response), session);
    }

    private async Task<Device> ResolveDeviceAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var key = DeviceKey();
        var device = await db.Devices.SingleOrDefaultAsync(d => d.UserId == userId && d.DeviceKey == key, ct);
        if (device is not null)
        {
            device.Seen(request.IpAddress, now);
            return device;
        }
        var hasOtherDevices = await db.Devices.AnyAsync(d => d.UserId == userId, ct);
        device = Device.Register(userId, key, DeviceNames.FromUserAgent(request.UserAgent), request.IpAddress, now, alertOwner: hasOtherDevices);
        db.Devices.Add(device);
        return device;
    }

    private UnauthorizedException Fail(Guid? userId, string email, string reason)
    {
        RecordAttempt(userId, email, succeeded: false, reason);
        audit.Record("auth.login_failed", nameof(User), userId, after: new { Email = email, Reason = reason }, actorUserId: userId);
        return new UnauthorizedException(InvalidCredentials);
    }

    private void RecordAttempt(Guid? userId, string email, bool succeeded, string? reason) =>
        db.LoginAttempts.Add(LoginAttempt.Record(userId, email, succeeded, reason, request.IpAddress, request.UserAgent, request.DeviceKey, Now()));

    private (RefreshToken Token, string Raw) NewRefreshToken(Guid userId, Guid familyId, Guid? deviceId, DateTime now)
    {
        var raw = tokens.NewRefreshToken();
        var token = RefreshToken.Issue(userId, tokens.HashRefreshToken(raw), familyId, deviceId, now, TimeSpan.FromDays(sessionOptions.RefreshTokenDays));
        return (token, raw);
    }

    private IssuedSession Issue(User user, string rawRefresh, RefreshToken refresh)
    {
        var access = tokens.CreateAccessToken(user);
        return new IssuedSession(new SessionResponse(access.Token, access.ExpiresAt, user.Name, user.Role), rawRefresh, refresh.ExpiresAt);
    }

    /// <summary>Without an X-Device-Id header, fall back to the user agent so the same browser is still recognized.</summary>
    private string DeviceKey() => request.DeviceKey
        ?? "ua:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.UserAgent ?? "unknown")))[..16];

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}

public static class RevokeReasons
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse_detected";
    public const string RevokedByUser = "revoked_by_user";
}
