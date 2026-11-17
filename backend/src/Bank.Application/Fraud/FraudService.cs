using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Application.Pix;
using Bank.Domain.Fraud;
using Bank.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Fraud;

public record FraudReviewResponse(
    Guid Id, decimal Amount, string PayeeName, string? Description, string Reasons, FraudReviewStatus Status, DateTime CreatedAt, DateTime? ReviewedAt);

public record FraudPolicyRequest(bool Enabled, decimal AverageMultiplier, decimal AverageRuleMinAmount, decimal NewDeviceAmount, int VelocityCount);

public class FraudService(IBankDbContext db, ICurrentUser currentUser, PixDirectory directory, PixPayments payments, IAuditLog audit, TimeProvider clock)
{
    public async Task<FraudPolicy> PolicyAsync(CancellationToken ct) => await db.FraudPolicies.SingleOrDefaultAsync(ct) ?? new FraudPolicy();

    /// <summary>Rules that a PIX from this account would trigger right now (empty = let it through).</summary>
    public async Task<IReadOnlyList<string>> EvaluatePixAsync(Guid accountId, Guid userId, decimal amount, CancellationToken ct)
    {
        var policy = await PolicyAsync(ct);
        if (!policy.Enabled) return [];
        var now = clock.GetUtcNow().UtcDateTime;
        var sent = db.PixTransfers.Where(p => p.PayerAccountId == accountId);
        var average = await sent.Where(p => p.CreatedAt >= now.AddDays(-90)).AverageAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var recent = await sent.CountAsync(p => p.CreatedAt >= now.AddMinutes(-10), ct);
        // "New device": seen for the first time in the last 24h on an account that already had other devices
        // (the device used at sign-up is not suspicious by itself).
        var key = currentUser.DeviceKey;
        var newDevice = key is not null
            && await db.Devices.AnyAsync(d => d.UserId == userId && d.DeviceKey == key && d.FirstSeenAt > now.AddHours(-24), ct)
            && await db.Devices.AnyAsync(d => d.UserId == userId && d.DeviceKey != key && d.FirstSeenAt <= now.AddHours(-24), ct);
        return policy.Evaluate(new FraudSignals(amount, average, recent, newDevice));
    }

    /// <summary>Holds the amount on the payer's balance and opens a review instead of paying.</summary>
    public async Task<FraudReview> HoldPixAsync(Guid accountId, Guid userId, PixRecipient payee, decimal amount, string? description,
        IReadOnlyList<string> reasons, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).Single();
        var now = clock.GetUtcNow().UtcDateTime;
        account.EnsureCanSend();
        // A payment over the limit is refused now, not after the analyst approves it.
        await payments.EnsureWithinLimitAsync(account, amount, now, ct);
        var hold =account.PlaceHold(amount, "Análise antifraude", now);
        db.BalanceHolds.Add(hold);
        var review = FraudReview.Hold(accountId, userId, amount, payee.AccountId, payee.Name, payee.KeyType, payee.KeyValue, description, hold.Id, reasons, now);
        db.FraudReviews.Add(review);
        audit.Record("fraud.pix_held", nameof(FraudReview), review.Id, after: new { amount, Reasons = review.Reasons });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return review;
    }

    public Task<List<FraudReviewResponse>> ListMineAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return Project(db.FraudReviews.Where(r => r.UserId == userId)).ToListAsync(ct);
    }

    public Task<PagedResult<FraudReviewResponse>> ListAsync(FraudReviewStatus status, int page, CancellationToken ct) =>
        Project(db.FraudReviews.Where(r => r.Status == status)).ToPagedAsync(page, 20, ct);

    /// <summary>The analyst confirms it was the customer: the hold is released and the PIX goes out in the same transaction.</summary>
    public async Task ApproveAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await db.FraudReviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw new NotFoundException("Análise não encontrada.");
        var payee = await directory.FindAsync(review.KeyValue, ct) ?? throw new NotFoundException("A chave do destinatário não existe mais.");
        var payerName = await db.Customers.Where(c => c.UserId == review.UserId).Select(c => c.FullName).SingleAsync(ct) ?? "";
        var reviewer = currentUser.UserId;
        // Limits were checked when the customer asked for the PIX; approving at night must not block a daytime payment.
        await payments.PayAsync(review.AccountId, payerName, payee, review.Amount, review.Description, ct, checkLimits: false,
            prepare: async (payer, now) =>
            {
                var tracked = await db.FraudReviews.SingleAsync(r => r.Id == reviewId, ct);
                if (tracked.Status != FraudReviewStatus.Pending) throw new Domain.Common.DomainException("fraud.decided", "Esta análise já foi concluída.");
                payer.ReleaseHold(await db.BalanceHolds.SingleAsync(h => h.Id == tracked.HoldId, ct), now);
                return pix =>
                {
                    tracked.Approve(reviewer, pix.Id, now);
                    audit.Record("fraud.review_approved", nameof(FraudReview), reviewId, after: new { pix.EndToEndId });
                };
            });
    }

    public async Task RejectAsync(Guid reviewId, CancellationToken ct)
    {
        var accountId = await db.FraudReviews.Where(r => r.Id == reviewId).Select(r => (Guid?)r.AccountId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Análise não encontrada.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var account = (await db.LockAccountsAsync([accountId], ct)).Single();
        var review = await db.FraudReviews.SingleAsync(r => r.Id == reviewId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        review.Reject(currentUser.UserId, now);
        account.ReleaseHold(await db.BalanceHolds.SingleAsync(h => h.Id == review.HoldId, ct), now);
        audit.Record("fraud.review_rejected", nameof(FraudReview), reviewId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<FraudPolicy> UpdatePolicyAsync(FraudPolicyRequest req, CancellationToken ct)
    {
        var policy = await db.FraudPolicies.SingleOrDefaultAsync(ct);
        if (policy is null) db.FraudPolicies.Add(policy = new FraudPolicy());
        policy.Update(req.Enabled, req.AverageMultiplier, req.AverageRuleMinAmount, req.NewDeviceAmount, req.VelocityCount);
        audit.Record("backoffice.fraud_policy_updated", nameof(FraudPolicy), policy.Id, after: req);
        await db.SaveChangesAsync(ct);
        return policy;
    }

    private static IQueryable<FraudReviewResponse> Project(IQueryable<FraudReview> q) =>
        q.OrderByDescending(r => r.CreatedAt).Select(r => new FraudReviewResponse(r.Id, r.Amount, r.PayeeName, r.Description, r.Reasons, r.Status, r.CreatedAt, r.ReviewedAt));
}

public class FraudReviewChangedHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<FraudReviewChanged>
{
    public async Task HandleAsync(FraudReviewChanged e, CancellationToken ct)
    {
        var (title, body) = e.Status switch
        {
            FraudReviewStatus.Pending => ("PIX em análise", $"Seu PIX de {Formatting.Brl(e.Amount)} para {e.PayeeName} está em análise de segurança. O valor fica reservado até a conclusão."),
            FraudReviewStatus.Approved => ("PIX liberado", $"Confirmamos seu PIX de {Formatting.Brl(e.Amount)} para {e.PayeeName}. Ele já foi enviado."),
            _ => ("PIX não realizado", $"Por segurança, seu PIX de {Formatting.Brl(e.Amount)} para {e.PayeeName} não foi feito. O valor voltou a ficar disponível."),
        };
        db.Notifications.Add(Notification.Create(e.UserId, title, body, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
