using System.Security.Claims;
using System.Text;
using Bank.Api.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bank.Api.Auth;

public record JwtOptions(string Issuer, string Key, int ExpiresMinutes = 60)
{
    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

public class TokenService(JwtOptions options)
{
    private readonly JsonWebTokenHandler _handler = new();

    public string Create(User user) => _handler.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = options.Issuer,
        Audience = options.Issuer,
        Expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes),
        Subject = new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
        ]),
        SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
    });
}

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
