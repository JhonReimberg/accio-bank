using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Boletos;
using Bank.Domain.Common;
using Bank.Domain.Customers;
using Bank.Domain.Ledger;
using Bank.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Boletos;

public record IssueBoletoRequest(decimal Amount, DateOnly DueDate, string? Description);

public record BoletoResponse(Guid Id, decimal Amount, DateOnly DueDate, string? Description, string DigitableLine, string Barcode, BoletoStatus Status, DateTime CreatedAt, DateTime? PaidAt);

public record BoletoInfo(string Barcode, decimal Amount, DateOnly? DueDate, string BeneficiaryName, bool IssuedHere, bool Overdue);

/// <param name="Amount">Only for boletos without a printed amount.</param>
/// <param name="ScheduledFor">Empty or today pays now; a future date schedules it.</param>
public record PayBoletoRequest(string Code, decimal? Amount, DateOnly? ScheduledFor, string Pin);

public record BoletoPaymentResponse(
    Guid Id, string BeneficiaryName, decimal Amount, DateOnly? DueDate, DateOnly ScheduledFor, BoletoPaymentStatus Status, string? FailureReason, DateTime CreatedAt, DateTime? PaidAt);

public record RechargeRequest(string Operator, string Phone, decimal Amount, string Pin);

public record RechargeResponse(Guid TransactionId, string Operator, string Phone, decimal Amount);

public class BoletoService(
    IBankDbContext db, ICurrentUser currentUser, TransactionAuthorizer authorizer, BoletoPayments payments, LedgerPosting ledger, IAuditLog audit, TimeProvider clock)
{
    private DateOnly Today => BankTime.Today(clock.GetUtcNow().UtcDateTime);

    public async Task<BoletoResponse> IssueAsync(IssueBoletoRequest req, CancellationToken ct)
    {
        var (accountId, name) = await MeAsync(ct);
        var boleto = Boleto.Issue(accountId, name, req.Amount, req.DueDate, req.Description?.Trim(), Today, clock.GetUtcNow().UtcDateTime, Random.Shared);
        db.Boletos.Add(boleto);
        audit.Record("boleto.issued", nameof(Boleto), boleto.Id, after: new { boleto.Amount, boleto.DueDate });
        await db.SaveChangesAsync(ct);
        return ToResponse(boleto);
    }

    public async Task<List<BoletoResponse>> ListIssuedAsync(CancellationToken ct)
    {
        var (accountId, _) = await MeAsync(ct);
        var boletos = await db.Boletos.AsNoTracking().Where(b => b.BeneficiaryAccountId == accountId).OrderByDescending(b => b.CreatedAt).Take(50).ToListAsync(ct);
        return boletos.Select(ToResponse).ToList();
    }

    public async Task CancelIssuedAsync(Guid id, CancellationToken ct)
    {
        var (accountId, _) = await MeAsync(ct);
        var boleto = await db.Boletos.SingleOrDefaultAsync(b => b.Id == id && b.BeneficiaryAccountId == accountId, ct) ?? throw new NotFoundException("Boleto não encontrado.");
        boleto.Cancel();
        audit.Record("boleto.cancelled", nameof(Boleto), boleto.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reads the code the customer typed: amount, due date and who receives (ours) before paying.</summary>
    public async Task<BoletoInfo> InspectAsync(string code, CancellationToken ct)
    {
        var data = BoletoCode.Parse(code, Today);
        var ours = await payments.FindIssuedAsync(data, ct);
        if (ours is { Status: not BoletoStatus.Open })
            throw new DomainException("boleto.not_open", ours.Status == BoletoStatus.Paid ? "Este boleto já foi pago." : "Este boleto foi cancelado.");
        return new BoletoInfo(data.Barcode, data.Amount, data.DueDate, ours?.BeneficiaryName ?? BoletoPayments.ExternalBeneficiary,
            ours is not null, data.DueDate < Today);
    }

    public async Task<BoletoPaymentResponse> PayAsync(PayBoletoRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var (accountId, _) = await MeAsync(ct);
        var data = BoletoCode.Parse(req.Code, Today);
        var ours = await payments.FindIssuedAsync(data, ct);
        if (ours?.BeneficiaryAccountId == accountId) throw new DomainException("boleto.own", "Você não pode pagar um boleto emitido por você.");

        var amount = data.Amount > 0 ? data.Amount : req.Amount ?? throw new DomainException("boleto.amount_required", "Informe o valor a pagar.");
        Money.EnsureValidAmount(amount);
        var date = req.ScheduledFor ?? Today;
        if (date < Today) throw new DomainException("boleto.past_date", "A data do pagamento não pode estar no passado.");
        if (data.DueDate is { } due && date > due) throw new DomainException("boleto.after_due", "Agende para no máximo a data de vencimento.");

        var payment = BoletoPayment.Create(accountId, data, ours?.BeneficiaryName ?? BoletoPayments.ExternalBeneficiary, amount, date, clock.GetUtcNow().UtcDateTime);
        db.BoletoPayments.Add(payment);
        audit.Record(date > Today ? "boleto.payment_scheduled" : "boleto.payment_requested", nameof(BoletoPayment), payment.Id, after: new { amount, date });
        await db.SaveChangesAsync(ct);
        if (date == Today) await payments.ExecuteAsync(payment.Id, throwOnFailure: true, ct);
        return ToResponse(await db.BoletoPayments.AsNoTracking().SingleAsync(p => p.Id == payment.Id, ct));
    }

    public async Task<List<BoletoPaymentResponse>> ListPaymentsAsync(CancellationToken ct)
    {
        var (accountId, _) = await MeAsync(ct);
        var list = await db.BoletoPayments.AsNoTracking().Where(p => p.PayerAccountId == accountId)
            .OrderByDescending(p => p.CreatedAt).Take(50).ToListAsync(ct);
        return list.Select(ToResponse).ToList();
    }

    public async Task CancelScheduledAsync(Guid id, CancellationToken ct)
    {
        var (accountId, _) = await MeAsync(ct);
        var payment = await db.BoletoPayments.SingleOrDefaultAsync(p => p.Id == id && p.PayerAccountId == accountId, ct) ?? throw new NotFoundException("Pagamento não encontrado.");
        payment.Cancel();
        audit.Record("boleto.payment_cancelled", nameof(BoletoPayment), payment.Id);
        await db.SaveChangesAsync(ct);
    }

    // --- Mobile recharge (simulated operators) ---

    public static readonly IReadOnlyList<string> Operators = ["Operadora Aurora", "Operadora Brisa", "Operadora Cometa", "Operadora Delta"];
    public static readonly IReadOnlyList<decimal> RechargeAmounts = [15, 20, 30, 50, 100];

    public async Task<RechargeResponse> RechargeAsync(RechargeRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        if (!Operators.Contains(req.Operator)) throw new DomainException("recharge.operator", "Operadora inválida.");
        if (!RechargeAmounts.Contains(req.Amount)) throw new DomainException("recharge.amount", "Escolha um dos valores de recarga.");
        var phone = Phone.TryNormalize(req.Phone);
        if (phone is null || phone.Length != 14) throw new DomainException("recharge.phone", "Informe um celular válido com DDD.");
        var (accountId, _) = await MeAsync(ct);
        var shown = $"({phone[3..5]}) {phone[5..10]}-{phone[10..]}";
        var tx = await ledger.ExecuteAsync([accountId, SystemAccounts.Cash], (accounts, now) =>
        {
            audit.Record("recharge.done", nameof(Account), accountId, after: new { req.Operator, req.Amount, Phone = shown });
            return LedgerTransaction.Transfer(TransactionKind.MobileRecharge, accounts[accountId], accounts[SystemAccounts.Cash], req.Amount,
                $"Recarga {req.Operator} {shown}", $"Repasse de recarga: {req.Operator}", now);
        }, ct);
        return new RechargeResponse(tx.Id, req.Operator, shown, req.Amount);
    }

    private async Task<(Guid AccountId, string Name)> MeAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var me = await (from a in db.Accounts
                        join c in db.Customers on a.UserId equals c.UserId
                        where a.UserId == userId
                        select new { a.Id, c.FullName }).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException("Conta não encontrada.");
        return (me.Id, me.FullName ?? "");
    }

    private static BoletoResponse ToResponse(Boleto b) =>
        new(b.Id, b.Amount, b.DueDate, b.Description, BoletoCode.Format(b.DigitableLine), b.Barcode, b.Status, b.CreatedAt, b.PaidAt);

    private static BoletoPaymentResponse ToResponse(BoletoPayment p) =>
        new(p.Id, p.BeneficiaryName, p.Amount, p.DueDate, p.ScheduledFor, p.Status, p.FailureReason, p.CreatedAt, p.PaidAt);
}

/// <summary>Moves the money of one boleto payment, now or when its scheduled date arrives.</summary>
public class BoletoPayments(IBankDbContext db, LedgerPosting ledger, IAuditLog audit)
{
    public const string ExternalBeneficiary = "Beneficiário de outro banco (simulado)";

    public Task<Boleto?> FindIssuedAsync(BoletoData data, CancellationToken ct) =>
        data.BankCode == BoletoCode.OwnBankCode ? db.Boletos.AsNoTracking().SingleOrDefaultAsync(b => b.Barcode == data.Barcode, ct) : Task.FromResult<Boleto?>(null);

    /// <summary>
    /// Ours: payer → beneficiary, and the boleto becomes paid (checked under the beneficiary lock, so it is paid once).
    /// Another bank's: payer → clearing (cash) account. A failure marks the payment failed and notifies the payer.
    /// </summary>
    public async Task ExecuteAsync(Guid paymentId, bool throwOnFailure, CancellationToken ct)
    {
        var payment = await db.BoletoPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId, ct);
        var beneficiaryAccount = await db.Boletos.Where(b => b.Barcode == payment.Barcode).Select(b => (Guid?)b.BeneficiaryAccountId).SingleOrDefaultAsync(ct);
        var target = beneficiaryAccount ?? SystemAccounts.Cash;
        try
        {
            await ledger.ExecuteIfNeededAsync([payment.PayerAccountId, target], async (accounts, now) =>
            {
                var tracked = await db.BoletoPayments.SingleAsync(p => p.Id == paymentId, ct);
                if (tracked.Status != BoletoPaymentStatus.Scheduled) return null; // cancelled, or paid by a parallel run
                accounts[payment.PayerAccountId].EnsureCanSend();
                if (beneficiaryAccount is not null)
                {
                    var boleto = await db.Boletos.SingleAsync(b => b.Barcode == payment.Barcode, ct);
                    boleto.MarkPaid(now);
                }
                var tx = LedgerTransaction.Transfer(TransactionKind.BoletoPayment, accounts[payment.PayerAccountId], accounts[target], payment.Amount,
                    $"Boleto pago: {payment.BeneficiaryName}", beneficiaryAccount is null ? "Liquidação de boleto (outro banco)" : "Boleto recebido", now);
                tracked.MarkPaid(tx.Id, now);
                audit.Record("boleto.paid", nameof(BoletoPayment), paymentId, after: new { payment.Amount }, bySystem: !throwOnFailure);
                return tx;
            }, ct);
        }
        catch (DomainException e)
        {
            db.DiscardChanges();
            var failed = await db.BoletoPayments.SingleAsync(p => p.Id == paymentId, ct);
            failed.Fail(e.Message);
            await db.SaveChangesAsync(ct);
            if (throwOnFailure) throw;
        }
    }
}

public class ExecuteScheduledBoletosJob(IBankDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExecuteScheduledBoletosJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var today = BankTime.Today(clock.GetUtcNow().UtcDateTime);
        var due = await db.BoletoPayments.Where(p => p.Status == BoletoPaymentStatus.Scheduled && p.ScheduledFor <= today).Select(p => p.Id).ToListAsync(ct);
        foreach (var id in due)
        {
            using var scope = scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<BoletoPayments>().ExecuteAsync(id, throwOnFailure: false, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Scheduled boleto payment {PaymentId} failed unexpectedly", id);
            }
        }
    }
}

public class BoletoPaymentFailedHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<BoletoPaymentFailed>
{
    public async Task HandleAsync(BoletoPaymentFailed e, CancellationToken ct)
    {
        var userId = await db.Accounts.Where(a => a.Id == e.PayerAccountId).Select(a => a.UserId).SingleAsync(ct);
        if (userId is null) return;
        db.Notifications.Add(Notification.Create(userId.Value, "Boleto não pago",
            $"O pagamento de {Formatting.Brl(e.Amount)} para {e.BeneficiaryName} não foi feito: {e.Reason}", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
