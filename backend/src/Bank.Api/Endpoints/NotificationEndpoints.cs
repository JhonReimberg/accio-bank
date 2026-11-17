using Bank.Application.Notifications;

namespace Bank.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();
        g.MapGet("/", async (NotificationService notifications, CancellationToken ct, int page = 1, int pageSize = 20) =>
            TypedResults.Ok(await notifications.ListMineAsync(page, pageSize, ct)));
        g.MapGet("/unread-count", async (NotificationService notifications, CancellationToken ct) =>
            TypedResults.Ok(new { count = await notifications.UnreadCountAsync(ct) }));
        g.MapPost("/read-all", async (NotificationService notifications, CancellationToken ct) =>
        {
            await notifications.MarkAllAsReadAsync(ct);
            return TypedResults.NoContent();
        });
    }
}
