using System.Security.Claims;
using Bank.Application.Abstractions;
using Bank.Application.Common;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bank.Api.Infrastructure;

internal class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => UserIdOrNull ?? throw new UnauthorizedException("Sessão inválida.");

    public Guid? UserIdOrNull =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var ua = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(ua) ? null : ua[..Math.Min(ua.Length, 300)];
        }
    }
}
