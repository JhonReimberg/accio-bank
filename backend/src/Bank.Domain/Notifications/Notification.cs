using Bank.Domain.Common;

namespace Bank.Domain.Notifications;

/// <summary>In-app notification shown in the notification center.</summary>
public class Notification : Entity
{
    private Notification() { }

    public Guid UserId { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public static Notification Create(Guid userId, string title, string body, DateTime now) =>
        new() { UserId = userId, Title = title, Body = body, CreatedAt = now };

    public void MarkAsRead(DateTime now) => ReadAt ??= now;
}
