using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bank.Application.Abstractions;
using Bank.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bank.Infrastructure.Security;

public record JwtOptions(string Issuer, string Key, int AccessTokenMinutes = 15)
{
    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));

    /// <summary>2FA challenge tokens use another audience, so they can never be used as access tokens.</summary>
    public string ChallengeAudience => $"{Issuer}:2fa";
}

public static class ClaimNames
{
    public const string Role = "role";
    public const string Device = "dev";
}

internal class TokenService(JwtOptions options, TimeProvider clock) : ITokenService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user)
    {
        var expires = Now().AddMinutes(options.AccessTokenMinutes);
        var token = Create(options.Issuer, expires,
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
            new Claim(ClaimNames.Role, user.Role.ToString()));
        return new AccessToken(token, expires);
    }

    public string CreateChallengeToken(Guid userId, string deviceKey) =>
        Create(options.ChallengeAudience, Now() + ChallengeLifetime,
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimNames.Device, deviceKey));

    public async Task<ChallengeClaims?> ReadChallengeTokenAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.ChallengeAudience,
            IssuerSigningKey = options.SigningKey,
            ClockSkew = TimeSpan.Zero,
        });
        if (!result.IsValid) return null;
        var identity = result.ClaimsIdentity;
        return Guid.TryParse(identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
               && identity.FindFirst(ClaimNames.Device)?.Value is { } device
            ? new ChallengeClaims(userId, device)
            : null;
    }

    public string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string Create(string audience, DateTime expires, params Claim[] claims)
    {
        var now = Now();
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
