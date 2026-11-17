using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Domain.Common;
using Bank.Domain.Notifications;
using Bank.Domain.Pix;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Pix;

public record CreatePixScheduleRequest(
    string Key, decimal Amount, string? Description, DateOnly FirstDate, ScheduleFrequency Frequency, DateOnly? EndDate, string Pin);

public record PixScheduleResponse(
    Guid Id, string RecipientName, PixKeyType KeyType, string MaskedKey, decimal Amount, string? Description,
    ScheduleFrequency Frequency, DateOnly NextRunDate, DateOnly? EndDate, ScheduleStatus Status, int Executions, string? LastError);

public class CreatePixScheduleRequestValidator : AbstractValidator<CreatePixScheduleRequest>
{
    public CreatePixScheduleRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100).WithName("Chave");
        RuleFor(x => x.Amount).MoneyAmount(max: 1_000_000).WithName("Valor");
        RuleFor(x => x.Description).MaximumLength(140).WithName("Mensagem");
        RuleFor(x => x.Pin).NotEmpty().WithName("Senha transacional");
    }
}

public class PixScheduleService(
    IBankDbContext db,
    ICurrentUser currentUser,
    PixDirectory directory,
    TransactionAuthorizer authorizer,
    IAuditLog audit,
    TimeProvider clock,
    IValidator<CreatePixScheduleRequest> validator)
{
    public async Task<PixScheduleResponse> CreateAsync(CreatePixScheduleRequest req, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(req, ct);
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var accountId = await MyAccountIdAsync(ct);
        var recipient = await directory.FindAsync(req.Key, ct) ?? throw new NotFoundException("Chave PIX não encontrada.");
        if (recipient.AccountId == accountId)
            throw new DomainException("pix.same_account", "Não é possível agendar um PIX para a própria conta.");

        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = PixSchedule.Create(accountId, recipient.KeyType, recipient.KeyValue, req.Amount, req.Description?.Trim(),
            req.Frequency, req.FirstDate, req.EndDate, DateOnly.FromDateTime(BankTime.ToLocal(now)), now);
        db.PixSchedules.Add(schedule);
        audit.Record("pix.scheduled", nameof(PixSchedule), schedule.Id, after: new { req.Amount, req.Frequency, req.FirstDate, req.EndDate });
        await db.SaveChangesAsync(ct);
        return ToResponse(schedule, recipient.Name);
    }

    public async Task<IReadOnlyList<PixScheduleResponse>> ListAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var schedules = await db.PixSchedules.AsNoTracking().Where(s => s.PayerAccountId == accountId)
            .OrderBy(s => s.Status).ThenBy(s => s.NextRunDate).ToListAsync(ct);
        // shortcut: one key lookup per schedule (N+1); fine for the handful a customer has, batch it if lists grow.
        var result = new List<PixScheduleResponse>();
        foreach (var s in schedules)
            result.Add(ToResponse(s, (await directory.FindAsync(s.KeyValue, ct))?.Name ?? "Chave removida"));
        return result;
    }

    public async Task CancelAsync(Guid scheduleId, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var schedule = await db.PixSchedules.SingleOrDefaultAsync(s => s.Id == scheduleId && s.PayerAccountId == accountId, ct)
            ?? throw new NotFoundException("Agendamento não encontrado.");
        schedule.Cancel();
        audit.Record("pix.schedule_cancelled", nameof(PixSchedule), schedule.Id);
        await db.SaveChangesAsync(ct);
    }

    private static PixScheduleResponse ToResponse(PixSchedule s, string recipientName) => new(
        s.Id, recipientName, s.KeyType, PixKeys.Mask(s.KeyType, s.KeyValue), s.Amount, s.Description, s.Frequency,
        s.NextRunDate, s.EndDate, s.Status, s.Executions, s.LastError);

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("Conta não encontrada.");
    }
}

/// <summary>Runs one due occurrence of a schedule. Scoped: one instance (and DbContext) per occurrence.</summary>
public class PixScheduleRunner(IBankDbContext db, PixDirectory directory, PixPayments payments, ILogger<PixScheduleRunner> logger)
{
    public async Task RunAsync(Guid scheduleId, DateOnly today, CancellationToken ct)
    {
        var schedule = await db.PixSchedules.SingleAsync(s => s.Id == scheduleId, ct);
        if (!schedule.IsDue(today)) return;
        var occurrence = schedule.NextRunDate;

        string? error;
        try
        {
            var payee = await directory.FindAsync(schedule.KeyValue, ct)
                ?? throw new DomainException("pix.key_not_found", "A chave PIX do destinatário não existe mais.");
            var payerName = await (
                    from a in db.Accounts
                    join u in db.Users on a.UserId equals u.Id
                    join c in db.Customers on u.Id equals c.UserId
                    where a.Id == schedule.PayerAccountId
                    select c.FullName ?? u.Name)
                .SingleAsync(ct);
            // The schedule advances in the same database transaction as the payment.
            await payments.PayAsync(schedule.PayerAccountId, payerName, payee, schedule.Amount, schedule.Description, ct,
                schedule.Id, occurrence, alsoInTransaction: _ => schedule.Advance(error: null));
            return;
        }
        catch (DomainException e)
        {
            error = e.Message;
        }
        catch (ConflictException)
        {
            // Unique (schedule, occurrence): another run already paid it.
            logger.LogInformation("Schedule {ScheduleId} occurrence {Date} already executed", scheduleId, occurrence);
            return;
        }

        // Payment refused (no balance, limit, blocked account...): not retried, the customer is told why.
        db.DiscardChanges();
        schedule = await db.PixSchedules.SingleAsync(s => s.Id == scheduleId, ct);
        schedule.Advance(error);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Background job: executes every scheduled PIX that is due today (Brasília date).</summary>
public class ExecuteScheduledPixJob(IBankDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExecuteScheduledPixJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(BankTime.ToLocal(clock.GetUtcNow().UtcDateTime));
        var due = await db.PixSchedules.Where(s => s.Status == ScheduleStatus.Active && s.NextRunDate <= today)
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in due)
        {
            using var scope = scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<PixScheduleRunner>().RunAsync(id, today, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One broken schedule must not stop the others; Hangfire logs and the next run retries it.
                logger.LogError(ex, "Scheduled PIX {ScheduleId} failed unexpectedly", id);
            }
        }
    }
}

public class ScheduledPixFailedHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<ScheduledPixFailed>
{
    public async Task HandleAsync(ScheduledPixFailed e, CancellationToken ct)
    {
        var userId = await db.Accounts.Where(a => a.Id == e.PayerAccountId).Select(a => a.UserId).SingleAsync(ct);
        if (userId is null) return;
        db.Notifications.Add(Notification.Create(userId.Value, "PIX agendado não realizado",
            $"O PIX de {Formatting.Brl(e.Amount)} agendado para {e.Date:dd/MM/yyyy} não foi feito: {e.Reason}", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
