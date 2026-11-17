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

    public string? UserAgent => Header("User-Agent", 300);

    public string? DeviceKey => Header("X-Device-Id", 100);

    private string? Header(string name, int maxLength)
    {
        var value = accessor.HttpContext?.Request.Headers[name].ToString().Trim();
        return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, maxLength)];
    }
}
