using Bank.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bank.Api.Infrastructure;

/// <summary>Server-to-client only: the app listens for "notification" and refreshes its data.</summary>
[Authorize]
public class NotificationHub : Hub;

/// <summary>SignalR users are keyed by the JWT "sub" claim (inbound claim mapping is off).</summary>
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

public class SignalRNotifier(IHubContext<NotificationHub> hub) : IRealtimeNotifier
{
    public Task NotifyAsync(Guid userId, Guid notificationId, string title, string body, CancellationToken ct) =>
        hub.Clients.User(userId.ToString()).SendAsync("notification", new { id = notificationId, title, body }, ct);
}
