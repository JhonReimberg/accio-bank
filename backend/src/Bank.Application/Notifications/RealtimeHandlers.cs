using Bank.Application.Abstractions;
using Bank.Domain.Notifications;

namespace Bank.Application.Notifications;

/// <summary>Every saved notification is also pushed live to the user's open apps.</summary>
public class PushNotificationHandler(IRealtimeNotifier notifier) : IDomainEventHandler<NotificationCreated>
{
    public Task HandleAsync(NotificationCreated e, CancellationToken ct) => notifier.NotifyAsync(e.UserId, e.NotificationId, e.Title, e.Body, ct);
}
