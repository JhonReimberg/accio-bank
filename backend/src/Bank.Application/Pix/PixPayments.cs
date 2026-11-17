using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Common;
using Bank.Domain.Ledger;
using Bank.Domain.Pix;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Pix;

public record PixOptions(int LimitIncreaseGraceHours = 24);

/// <summary>Core of a PIX payment, shared by the app (send) and the scheduler job.</summary>
public class PixPayments(IBankDbContext db, LedgerPosting ledger, IAuditLog audit)
{
    public async Task<PixTransfer> PayAsync(
        Guid payerAccountId, string payerName, PixRecipient payee, decimal amount, string? description, CancellationToken ct,
        Guid? scheduleId = null, DateOnly? occurrence = null, Action? alsoInTransaction = null)
    {
        if (payee.AccountId == payerAccountId)
            throw new DomainException("pix.same_account", "Não é possível fazer um PIX para a própria conta.");

        PixTransfer? pix = null;
        await ledger.ExecuteAsync([payerAccountId, payee.AccountId], async (accounts, now) =>
        {
            var payer = accounts[payerAccountId];
            payer.EnsureCanSend();

            // Runs with the payer row locked, so parallel PIX cannot each see the same "used" amount.
            var limits = await LimitsAsync(payerAccountId, payer.Tier, ct);
            var period = PixPeriod.For(now, BankTime.SaoPaulo);
            var used = await db.PixTransfers
                .Where(p => p.PayerAccountId == payerAccountId && p.CreatedAt >= period.StartUtc && p.CreatedAt < period.EndUtc)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            limits.EnsureWithinLimit(period, used, amount, now);

            var transaction = LedgerTransaction.Transfer(
                TransactionKind.Pix, payer, accounts[payee.AccountId], amount,
                $"PIX enviado para {payee.Name}", $"PIX recebido de {payerName}", now);
            pix = PixTransfer.Record(payerAccountId, payee.AccountId, amount, description, payee.KeyType, payee.KeyValue,
                transaction.Id, now, Random.Shared, scheduleId, occurrence);
            db.PixTransfers.Add(pix);
            alsoInTransaction?.Invoke();
            audit.Record("pix.sent", nameof(PixTransfer), pix.Id,
                after: new { pix.EndToEndId, amount, Payee = payee.AccountId, scheduleId }, bySystem: scheduleId is not null);
            return transaction;
        }, ct);
        return pix!;
    }

    /// <summary>Limit settings are created on first use with the defaults of the account tier.</summary>
    public async Task<PixLimitSettings> LimitsAsync(Guid accountId, Domain.Accounts.AccountTier tier, CancellationToken ct)
    {
        var limits = await db.PixLimits.SingleOrDefaultAsync(l => l.AccountId == accountId, ct);
        if (limits is not null) return limits;
        limits = PixLimitSettings.ForTier(accountId, tier);
        db.PixLimits.Add(limits);
        return limits;
    }
}
