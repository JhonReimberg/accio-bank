using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Domain.Common;
using Bank.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bank.Infrastructure.Outbox;

public record OutboxOptions(bool Enabled = true, int PollIntervalMs = 1000, int BatchSize = 20, int MaxAttempts = 5);

/// <summary>
/// Delivers outbox messages to their handlers. Each message is handled inside the same database
/// transaction that marks it processed, so a handler's own database writes happen exactly once.
/// Several instances can run in parallel thanks to FOR UPDATE SKIP LOCKED.
/// </summary>
internal class OutboxProcessor(IServiceScopeFactory scopes, OutboxOptions options, TimeProvider clock, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox batch failed");
            }
            if (processed > 0) continue;
            try
            {
                // Real time on purpose: the business clock may be a fake one in tests.
                await Task.Delay(TimeSpan.FromMilliseconds(options.PollIntervalMs), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND attempts < {options.MaxAttempts}
                ORDER BY occurred_at
                LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            await tx.CreateSavepointAsync("message", ct);
            try
            {
                await DispatchAsync(scope.ServiceProvider, message, ct);
                message.MarkProcessed(clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
                await tx.RollbackToSavepointAsync("message", ct);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is not OutboxMessage).ToList())
                    entry.State = EntityState.Detached;
                message.MarkFailed(ex.Message);
                await db.SaveChangesAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        return messages.Count;
    }

    private static async Task DispatchAsync(IServiceProvider services, OutboxMessage message, CancellationToken ct)
    {
        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown event type {message.Type}.");
        var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType, Json)!;
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        foreach (var handler in services.GetServices(handlerType))
            await (Task)handle.Invoke(handler, [domainEvent, ct])!;
    }

    internal static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
