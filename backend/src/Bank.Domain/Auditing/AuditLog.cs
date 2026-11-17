using Bank.Domain.Common;

namespace Bank.Domain.Auditing;

/// <summary>Who did what, when, from where, with the values before and after. Append-only.</summary>
public class AuditLog : Entity
{
    private AuditLog() { }

    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public string? EntityId { get; private set; }
    /// <summary>JSON snapshot.</summary>
    public string? Before { get; private set; }
    /// <summary>JSON snapshot.</summary>
    public string? After { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static AuditLog Create(
        Guid? actorUserId, string action, string entityType, string? entityId, string? before, string? after, string? ipAddress, DateTime now) => new()
    {
        ActorUserId = actorUserId,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Before = before,
        After = after,
        IpAddress = ipAddress,
        CreatedAt = now,
    };
}
