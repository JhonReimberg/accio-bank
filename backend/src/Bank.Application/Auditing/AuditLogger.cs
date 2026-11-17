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
    /// <param name="actorUserId">Who acted; defaults to the authenticated user.</param>
    /// <param name="bySystem">An automatic action (jobs, auto-approval): recorded with no human actor.</param>
    void Record(string action, string entityType, object? entityId, object? before = null, object? after = null,
        Guid? actorUserId = null, bool bySystem = false);
}

internal class AuditLogger(IBankDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, object? entityId, object? before = null, object? after = null,
        Guid? actorUserId = null, bool bySystem = false) =>
        db.AuditLogs.Add(AuditLog.Create(
            bySystem ? null : actorUserId ?? currentUser.UserIdOrNull,
            action,
            entityType,
            entityId?.ToString(),
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            currentUser.IpAddress,
            clock.GetUtcNow().UtcDateTime));
}
