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
}

public interface ITokenService
{
    string CreateAccessToken(User user);
}

/// <summary>One-way hashing for passwords and PINs.</summary>
public interface ISecretHasher
{
    string Hash(string secret);
    bool Verify(string hash, string secret);
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
