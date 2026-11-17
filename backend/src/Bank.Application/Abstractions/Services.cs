using Bank.Domain.Common;
using Bank.Domain.Users;

namespace Bank.Application.Abstractions;

/// <summary>Who is calling and from where.</summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user; throws when the request is anonymous.</summary>
    Guid UserId { get; }
    Guid? UserIdOrNull { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    /// <summary>Client-generated device id (X-Device-Id header), used for trusted devices.</summary>
    string? DeviceKey { get; }
}

public record AccessToken(string Token, DateTime ExpiresAt);

public record ChallengeClaims(Guid UserId, string DeviceKey);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>Short-lived token proving the password step passed; exchanged for a session with the 2FA code.</summary>
    string CreateChallengeToken(Guid userId, string deviceKey);

    Task<ChallengeClaims?> ReadChallengeTokenAsync(string token);

    /// <summary>Cryptographically random opaque token.</summary>
    string NewRefreshToken();

    string HashRefreshToken(string token);
}

/// <summary>One-way hashing for passwords and PINs.</summary>
public interface ISecretHasher
{
    string Hash(string secret);
    bool Verify(string hash, string secret);
}

/// <summary>Reversible encryption for secrets that must be read back (TOTP seeds).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

public interface ITotpService
{
    string NewSecret();
    bool Verify(string secret, string code, DateTime now);
    string ProvisioningUri(string accountName, string secret);
}

public interface IQrCodeGenerator
{
    /// <summary>PNG image of the QR code, base64 encoded.</summary>
    string PngBase64(string payload);
}

public record CepAddress(string Cep, string Street, string Neighborhood, string City, string State);

/// <summary>Address lookup by CEP (postal code).</summary>
public interface ICepLookup
{
    /// <summary>Null when the CEP does not exist or the provider is unavailable (the user then types the address).</summary>
    Task<CepAddress?> LookupAsync(string cep, CancellationToken ct);
}

/// <summary>Private file storage (KYC documents, generated PDFs).</summary>
public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Reacts to a domain event read from the outbox. Must be idempotent: delivery is at-least-once.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}
