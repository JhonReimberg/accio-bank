using Bank.Domain.Users;

namespace Bank.Application.Abstractions;

/// <summary>The authenticated user of the current request.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
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
