using Bank.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Bank.Infrastructure.Security;

/// <summary>PBKDF2 from ASP.NET Core Identity, for passwords and transaction PINs.</summary>
internal class SecretHasher : ISecretHasher
{
    private static readonly object Owner = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string secret) => _hasher.HashPassword(Owner, secret);

    public bool Verify(string hash, string secret) =>
        _hasher.VerifyHashedPassword(Owner, hash, secret) != PasswordVerificationResult.Failed;
}
