using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Domain.Auditing;

namespace Bank.Application.Auditing;

public interface IAuditLog
{
    /// <summary>
    /// Adds an audit entry to the current unit of work, so it is committed (or rolled back)
    /// together with the change it describes.
    /// </summary>
    void Record(string action, string entityType, object? entityId, object? before = null, object? after = null, Guid? actorUserId = null);
}

internal class AuditLogger(IBankDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, object? entityId, object? before = null, object? after = null, Guid? actorUserId = null) =>
        db.AuditLogs.Add(AuditLog.Create(
            actorUserId ?? currentUser.UserIdOrNull,
            action,
            entityType,
            entityId?.ToString(),
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            currentUser.IpAddress,
            clock.GetUtcNow().UtcDateTime));
}
