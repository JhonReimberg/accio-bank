using Bank.Domain.Common;

namespace Bank.Domain.Users;

/// <summary>
/// Long-lived token used to get new access tokens. Only its hash is stored. Every use rotates it;
/// tokens rotated from the same login share a FamilyId so that reuse of an old token (a sign of
/// theft) can revoke the whole family.
/// </summary>
public class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public Guid FamilyId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, Guid familyId, Guid? deviceId, DateTime now, TimeSpan lifetime) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        FamilyId = familyId,
        DeviceId = deviceId,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, string reason, Guid? replacedById = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
        ReplacedById = replacedById;
    }
}

/// <summary>A browser/app the user signed in from. Trusted devices skip the 2FA step.</summary>
public class Device : Entity
{
    private Device() { }

    public Guid UserId { get; private set; }
    /// <summary>Random id generated and kept by the client.</summary>
    public string DeviceKey { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool Trusted { get; private set; }
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public string? LastIpAddress { get; private set; }

    /// <param name="alertOwner">False for the very first device (sign-up), true afterwards.</param>
    public static Device Register(Guid userId, string deviceKey, string name, string? ip, DateTime now, bool alertOwner)
    {
        var device = new Device
        {
            UserId = userId,
            DeviceKey = deviceKey,
            Name = name,
            FirstSeenAt = now,
            LastSeenAt = now,
            LastIpAddress = ip,
        };
        if (alertOwner) device.Raise(new NewDeviceSignIn(userId, name, ip, now));
        return device;
    }

    public void Seen(string? ip, DateTime now)
    {
        LastSeenAt = now;
        LastIpAddress = ip;
    }

    public void Trust() => Trusted = true;
}

/// <summary>Access history entry: every sign-in attempt, successful or not.</summary>
public class LoginAttempt : Entity
{
    private LoginAttempt() { }

    public Guid? UserId { get; private set; }
    public string Email { get; private set; } = "";
    public bool Succeeded { get; private set; }
    public string? FailureReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? DeviceKey { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static LoginAttempt Record(
        Guid? userId, string email, bool succeeded, string? failureReason, string? ip, string? userAgent, string? deviceKey, DateTime now) => new()
    {
        UserId = userId,
        Email = email,
        Succeeded = succeeded,
        FailureReason = failureReason,
        IpAddress = ip,
        UserAgent = userAgent,
        DeviceKey = deviceKey,
        CreatedAt = now,
    };
}

public sealed record NewDeviceSignIn(Guid UserId, string DeviceName, string? IpAddress, DateTime At) : IDomainEvent;
