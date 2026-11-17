using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Admin;

public record PlaceHoldRequest(decimal Amount, string Reason);

public record HoldResponse(Guid Id, Guid AccountId, decimal Amount, string Reason, HoldStatus Status, DateTime CreatedAt);

public record AuditLogQuery(Guid? ActorUserId, string? Action, string? EntityId, DateTime? From, DateTime? To, int Page = 1, int PageSize = 20);

public record AuditLogResponse(
    Guid Id, Guid? ActorUserId, string Action, string EntityType, string? EntityId, string? Before, string? After, string? IpAddress, DateTime CreatedAt);

public class PlaceHoldRequestValidator : AbstractValidator<PlaceHoldRequest>
{
    public PlaceHoldRequestValidator()
    {
        RuleFor(x => x.Amount).MoneyAmount(max: 10_000_000).WithName("Valor");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200).WithName("Motivo");
    }
}

/// <summary>Back-office operations. Authorization (Analyst/Admin) is enforced at the API.</summary>
public class AdminService(
    IBankDbContext db,
    IAuditLog audit,
    TimeProvider clock,
    ReconciliationService reconciliation,
    IValidator<PlaceHoldRequest> holdValidator)
{
    public async Task<HoldResponse> PlaceHoldAsync(Guid accountId, PlaceHoldRequest request, CancellationToken ct)
    {
        await holdValidator.ValidateAndThrowAsync(request, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).SingleOrDefault()
            ?? throw new NotFoundException("Conta não encontrada.");
        var before = new { account.BlockedBalance, account.AvailableBalance };
        var hold = account.PlaceHold(request.Amount, request.Reason.Trim(), Now());
        db.BalanceHolds.Add(hold);
        audit.Record("admin.hold.placed", nameof(Account), account.Id, before,
            new { account.BlockedBalance, account.AvailableBalance, HoldId = hold.Id, hold.Amount, hold.Reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToResponse(hold);
    }

    public async Task<HoldResponse> ReleaseHoldAsync(Guid holdId, CancellationToken ct)
    {
        var accountId = await db.BalanceHolds.Where(h => h.Id == holdId).Select(h => (Guid?)h.AccountId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Bloqueio não encontrado.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).Single();
        var hold = await db.BalanceHolds.SingleAsync(h => h.Id == holdId, ct);
        var before = new { account.BlockedBalance, account.AvailableBalance };
        account.ReleaseHold(hold, Now());
        audit.Record("admin.hold.released", nameof(Account), account.Id, before,
            new { account.BlockedBalance, account.AvailableBalance, HoldId = hold.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToResponse(hold);
    }

    public Task<ReconciliationReport> ReconcileAsync(CancellationToken ct) => reconciliation.RunAsync(ct);

    public Task<PagedResult<AuditLogResponse>> SearchAuditLogsAsync(AuditLogQuery q, CancellationToken ct)
    {
        var query = db.AuditLogs.AsQueryable();
        if (q.ActorUserId is { } actor) query = query.Where(a => a.ActorUserId == actor);
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action.StartsWith(q.Action));
        if (!string.IsNullOrWhiteSpace(q.EntityId)) query = query.Where(a => a.EntityId == q.EntityId);
        if (q.From is { } from) query = query.Where(a => a.CreatedAt >= from);
        if (q.To is { } to) query = query.Where(a => a.CreatedAt < to);
        return query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditLogResponse(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.Before, a.After, a.IpAddress, a.CreatedAt))
            .ToPagedAsync(q.Page, q.PageSize, ct);
    }

    private static HoldResponse ToResponse(BalanceHold h) => new(h.Id, h.AccountId, h.Amount, h.Reason, h.Status, h.CreatedAt);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
