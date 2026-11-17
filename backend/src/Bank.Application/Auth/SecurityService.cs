using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Common;
using Bank.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Auth;

public record TwoFactorSetupResponse(string Secret, string OtpAuthUri, string QrCodePngBase64);

public record TwoFactorCodeRequest(string Code);

public record SetPinRequest(string Password, string Pin);

public record DeviceResponse(Guid Id, string Name, bool Trusted, bool Current, DateTime FirstSeenAt, DateTime LastSeenAt, string? LastIpAddress);

public record LoginAttemptResponse(DateTime CreatedAt, bool Succeeded, string? FailureReason, string? IpAddress, string? Device);

public record SecurityOverview(bool TwoFactorEnabled, bool HasTransactionPin);

public class SetPinRequestValidator : AbstractValidator<SetPinRequest>
{
    public SetPinRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().WithName("Senha de acesso");
        RuleFor(x => x.Pin).NotEmpty().WithName("Senha transacional");
    }
}

/// <summary>The customer's own security settings: 2FA, transaction PIN, devices, sessions and access history.</summary>
public class SecurityService(
    IBankDbContext db,
    ICurrentUser currentUser,
    ISecretHasher hasher,
    ITotpService totp,
    ISecretProtector protector,
    IQrCodeGenerator qrCodes,
    IAuditLog audit,
    TimeProvider clock,
    IValidator<SetPinRequest> pinValidator)
{
    public async Task<SecurityOverview> GetOverviewAsync(CancellationToken ct)
    {
        var user = await MeAsync(ct);
        return new SecurityOverview(user.TwoFactorEnabled, user.HasTransactionPin);
    }

    public async Task<TwoFactorSetupResponse> BeginTwoFactorSetupAsync(CancellationToken ct)
    {
        var user = await MeAsync(ct);
        var secret = totp.NewSecret();
        user.BeginTwoFactorSetup(protector.Protect(secret));
        await db.SaveChangesAsync(ct);
        var uri = totp.ProvisioningUri(user.Email, secret);
        return new TwoFactorSetupResponse(secret, uri, qrCodes.PngBase64(uri));
    }

    public async Task EnableTwoFactorAsync(TwoFactorCodeRequest req, CancellationToken ct)
    {
        var user = await MeAsync(ct);
        if (user.TwoFactorSecret is null || !totp.Verify(protector.Unprotect(user.TwoFactorSecret), req.Code, Now()))
            throw new DomainException("2fa.invalid_code", "Código inválido. Confira o horário do celular e tente novamente.");
        user.EnableTwoFactor();
        audit.Record("security.2fa_enabled", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task DisableTwoFactorAsync(TwoFactorCodeRequest req, CancellationToken ct)
    {
        var user = await MeAsync(ct);
        if (!user.TwoFactorEnabled || !totp.Verify(protector.Unprotect(user.TwoFactorSecret!), req.Code, Now()))
            throw new DomainException("2fa.invalid_code", "Código inválido.");
        user.DisableTwoFactor();
        audit.Record("security.2fa_disabled", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetTransactionPinAsync(SetPinRequest req, CancellationToken ct)
    {
        await pinValidator.ValidateAndThrowAsync(req, ct);
        var user = await MeAsync(ct);
        if (!hasher.Verify(user.PasswordHash, req.Password))
            throw new DomainException("auth.wrong_password", "Senha de acesso incorreta.");
        var hadPin = user.HasTransactionPin;
        user.SetTransactionPin(req.Pin, hasher.Hash);
        audit.Record(hadPin ? "security.pin_changed" : "security.pin_created", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceResponse>> ListDevicesAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var currentKey = currentUser.DeviceKey;
        return await db.Devices.Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new DeviceResponse(d.Id, d.Name, d.Trusted, d.DeviceKey == currentKey, d.FirstSeenAt, d.LastSeenAt, d.LastIpAddress))
            .ToListAsync(ct);
    }

    public async Task TrustDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await MyDeviceAsync(deviceId, ct);
        device.Trust();
        audit.Record("security.device_trusted", nameof(Device), device.Id, after: new { device.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Forgets a device and signs it out (its refresh tokens are revoked).</summary>
    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await MyDeviceAsync(deviceId, ct);
        await RevokeTokensAsync(t => t.DeviceId == device.Id, ct);
        db.Devices.Remove(device);
        audit.Record("security.device_removed", nameof(Device), device.Id, before: new { device.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllSessionsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        await RevokeTokensAsync(_ => true, ct);
        audit.Record("security.sessions_revoked", nameof(User), userId);
        await db.SaveChangesAsync(ct);
    }

    public Task<PagedResult<LoginAttemptResponse>> AccessHistoryAsync(int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return db.LoginAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new LoginAttemptResponse(
                a.CreatedAt, a.Succeeded, a.FailureReason, a.IpAddress,
                db.Devices.Where(d => d.UserId == userId && d.DeviceKey == a.DeviceKey).Select(d => d.Name).FirstOrDefault()))
            .ToPagedAsync(page, pageSize, ct);
    }

    private Task RevokeTokensAsync(System.Linq.Expressions.Expression<Func<RefreshToken, bool>> filter, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var now = Now();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).Where(filter)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokeReasons.RevokedByUser), ct);
    }

    private async Task<Device> MyDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, ct)
            ?? throw new NotFoundException("Dispositivo não encontrado.");
    }

    private async Task<User> MeAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new UnauthorizedException("Sessão inválida.");
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
