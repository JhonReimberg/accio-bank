using System.Security.Claims;
using Bank.Application.Abstractions;
using Bank.Application.Common;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bank.Api.Infrastructure;

internal class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new UnauthorizedException("Sessão inválida.");
}
