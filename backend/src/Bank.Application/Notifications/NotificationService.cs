using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Ledger;
using Bank.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Notifications;

public record NotificationResponse(Guid Id, string Title, string Body, DateTime CreatedAt, bool Read);

public class NotificationService(IBankDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public Task<PagedResult<NotificationResponse>> ListMineAsync(int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationResponse(n.Id, n.Title, n.Body, n.CreatedAt, n.ReadAt != null))
            .ToPagedAsync(page, pageSize, ct);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
    }

    public async Task MarkAllAsReadAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}

/// <summary>"You received money" notification, created from the outbox after the transfer committed.</summary>
public class FundsCreditedHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<FundsCredited>
{
    public async Task HandleAsync(FundsCredited e, CancellationToken ct)
    {
        var userId = await db.Accounts.Where(a => a.Id == e.AccountId).Select(a => a.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return;
        var title = e.Kind == TransactionKind.Deposit ? "Depósito recebido" : "Dinheiro na conta";
        db.Notifications.Add(Notification.Create(userId.Value, title, $"{e.Description}: {Formatting.Brl(e.Amount)}", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
