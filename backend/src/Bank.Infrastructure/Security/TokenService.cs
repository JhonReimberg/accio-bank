using System.Security.Claims;
using System.Text;
using Bank.Application.Abstractions;
using Bank.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bank.Infrastructure.Security;

public record JwtOptions(string Issuer, string Key, int AccessTokenMinutes = 15)
{
    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

internal class TokenService(JwtOptions options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public string CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(options.AccessTokenMinutes),
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
            ]),
            SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
