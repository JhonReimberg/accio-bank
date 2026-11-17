using Bank.Application.Abstractions;
using Bank.Application.Accounts;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Ledger;
using Bank.Domain.Pix;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Pix;

/// <param name="Key">PIX key typed by the payer, or...</param>
/// <param name="BrCode">...a "copia e cola" code. If the code has an amount, it is the amount paid.</param>
public record PixTransferRequest(string? Key, string? BrCode, decimal? Amount, string? Description, string Pin);

public record PixTransferResponse(Guid Id, string EndToEndId, Guid TransactionId, decimal Amount, string RecipientName, DateTime CreatedAt);

public record PixRefundRequest(decimal Amount, string Reason, string Pin);

public record PixRefundResponse(Guid Id, string EndToEndId, Guid TransactionId, decimal Amount, decimal RemainingRefundable);

public record PixTransferDetail(
    Guid Id, string EndToEndId, decimal Amount, decimal RefundedAmount, decimal RefundableAmount, string? Description,
    bool Received, bool CanRefund, DateTime CreatedAt);

public record CreateQrCodeRequest(Guid? KeyId, decimal? Amount, string? Description);

public record QrCodeResponse(string Payload, string QrCodePngBase64, string MaskedKey, decimal? Amount);

public record DecodeBrCodeRequest(string Payload);

public record DecodedBrCodeResponse(decimal? Amount, string? Description, PixRecipientResponse Recipient);

public record PixLimitsResponse(
    decimal DaytimeLimit, decimal NighttimeLimit, decimal? PendingDaytimeLimit, decimal? PendingNighttimeLimit, DateTime? PendingEffectiveAt,
    decimal MaxDaytimeLimit, decimal MaxNighttimeLimit, bool IsNightNow, decimal UsedInCurrentPeriod, decimal AvailableInCurrentPeriod);

public record ChangePixLimitsRequest(decimal DaytimeLimit, decimal NighttimeLimit, string Pin);

public class PixTransferRequestValidator : AbstractValidator<PixTransferRequest>
{
    public PixTransferRequestValidator()
    {
        RuleFor(x => x).Must(x => string.IsNullOrWhiteSpace(x.Key) != string.IsNullOrWhiteSpace(x.BrCode))
            .WithName("Destino").WithMessage("Informe uma chave PIX ou um código copia e cola.");
        RuleFor(x => x.Key).MaximumLength(100).WithName("Chave");
        RuleFor(x => x.BrCode).MaximumLength(512).WithName("Código PIX");
        RuleFor(x => x.Amount!.Value).MoneyAmount(max: 1_000_000).WithName("Valor").When(x => x.Amount is not null);
        RuleFor(x => x.Amount).NotNull().WithName("Valor").When(x => string.IsNullOrWhiteSpace(x.BrCode));
        RuleFor(x => x.Description).MaximumLength(140).WithName("Mensagem");
        RuleFor(x => x.Pin).NotEmpty().WithName("Senha transacional");
    }
}

public class PixRefundRequestValidator : AbstractValidator<PixRefundRequest>
{
    public PixRefundRequestValidator()
    {
        RuleFor(x => x.Amount).MoneyAmount(max: 1_000_000).WithName("Valor");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(140).WithName("Motivo");
        RuleFor(x => x.Pin).NotEmpty().WithName("Senha transacional");
    }
}

public class CreateQrCodeRequestValidator : AbstractValidator<CreateQrCodeRequest>
{
    public CreateQrCodeRequestValidator()
    {
        RuleFor(x => x.Amount!.Value).MoneyAmount(max: 1_000_000).WithName("Valor").When(x => x.Amount is not null);
        RuleFor(x => x.Description).MaximumLength(40).WithName("Mensagem");
    }
}

public class PixService(
    IBankDbContext db,
    ICurrentUser currentUser,
    PixDirectory directory,
    PixPayments payments,
    LedgerPosting ledger,
    TransactionAuthorizer authorizer,
    IQrCodeGenerator qrCodes,
    IAuditLog audit,
    PixOptions options,
    TimeProvider clock,
    IValidator<PixTransferRequest> transferValidator,
    IValidator<PixRefundRequest> refundValidator,
    IValidator<CreateQrCodeRequest> qrValidator)
{
    public async Task<PixRecipientResponse> LookupAsync(string key, CancellationToken ct) =>
        (await directory.FindAsync(key, ct) ?? throw new NotFoundException("Chave PIX não encontrada.")).ToResponse();

    public async Task<PixTransferResponse> SendAsync(PixTransferRequest req, CancellationToken ct)
    {
        await transferValidator.ValidateAndThrowAsync(req, ct);
        await authorizer.VerifyPinAsync(req.Pin, ct);

        var key = req.Key;
        var amount = req.Amount;
        var description = req.Description;
        if (!string.IsNullOrWhiteSpace(req.BrCode))
        {
            var code = BrCode.Parse(req.BrCode);
            if (code.Amount is { } fixedAmount && amount is not null && amount != fixedAmount)
                throw new DomainException("brcode.amount_mismatch", "O valor deste código PIX é fixo.");
            key = code.Key;
            amount = code.Amount ?? amount ?? throw new DomainException("brcode.amount_required", "Informe o valor do PIX.");
            description ??= code.Description;
        }

        var payee = await directory.FindAsync(key!, ct) ?? throw new NotFoundException("Chave PIX não encontrada.");
        var me = await MeAsync(ct);
        var pix = await payments.PayAsync(me.AccountId, me.Name, payee, amount!.Value, description?.Trim(), ct);
        return new PixTransferResponse(pix.Id, pix.EndToEndId, pix.LedgerTransactionId, pix.Amount, payee.Name, pix.CreatedAt);
    }

    /// <summary>Detail of a PIX found in the statement (by its ledger transaction), including whether it can be refunded.</summary>
    public async Task<PixTransferDetail> GetByTransactionAsync(Guid ledgerTransactionId, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var pix = await db.PixTransfers.AsNoTracking()
            .SingleOrDefaultAsync(p => p.LedgerTransactionId == ledgerTransactionId && (p.PayerAccountId == me.AccountId || p.PayeeAccountId == me.AccountId), ct)
            ?? throw new NotFoundException("PIX não encontrado.");
        var received = pix.PayeeAccountId == me.AccountId;
        var withinWindow = clock.GetUtcNow().UtcDateTime - pix.CreatedAt <= PixTransfer.RefundWindow;
        return new PixTransferDetail(pix.Id, pix.EndToEndId, pix.Amount, pix.RefundedAmount, pix.RefundableAmount, pix.Description,
            received, received && withinWindow && pix.RefundableAmount > 0, pix.CreatedAt);
    }

    /// <summary>The receiver gives back all or part of a PIX (e.g. wrong amount or wrong person).</summary>
    public async Task<PixRefundResponse> RefundAsync(Guid pixId, PixRefundRequest req, CancellationToken ct)
    {
        await refundValidator.ValidateAndThrowAsync(req, ct);
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var me = await MeAsync(ct);
        var original = await db.PixTransfers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pixId && p.PayeeAccountId == me.AccountId, ct)
            ?? throw new NotFoundException("PIX não encontrado.");
        var payerName = await NameOfAccountAsync(original.PayerAccountId, ct);

        PixRefund? refund = null;
        PixTransfer? pix = null;
        var transaction = await ledger.ExecuteAsync([me.AccountId, original.PayerAccountId], async (accounts, now) =>
        {
            // Re-read under the account locks: concurrent refunds of the same PIX are serialized here.
            pix = await db.PixTransfers.Include(p => p.Refunds).SingleAsync(p => p.Id == pixId, ct);
            var tx = LedgerTransaction.Transfer(TransactionKind.PixRefund, accounts[me.AccountId], accounts[original.PayerAccountId], req.Amount,
                $"Devolução de PIX para {payerName}", $"Devolução de PIX de {me.Name}", now);
            refund = pix.Refund(req.Amount, req.Reason.Trim(), tx.Id, now, Random.Shared);
            audit.Record("pix.refunded", nameof(PixTransfer), pix.Id, after: new { refund.EndToEndId, req.Amount, req.Reason });
            return tx;
        }, ct);
        return new PixRefundResponse(refund!.Id, refund.EndToEndId, transaction.Id, refund.Amount, pix!.RefundableAmount);
    }

    public async Task<QrCodeResponse> CreateQrCodeAsync(CreateQrCodeRequest req, CancellationToken ct)
    {
        await qrValidator.ValidateAndThrowAsync(req, ct);
        var me = await MeAsync(ct);
        var keys = db.PixKeys.Where(k => k.AccountId == me.AccountId);
        var key = await (req.KeyId is { } id ? keys.Where(k => k.Id == id) : keys.OrderBy(k => k.CreatedAt)).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("pix.no_key", "Cadastre uma chave PIX para receber por QR Code.");
        var city = await db.Customers.Where(c => c.UserId == currentUser.UserId).Select(c => c.Address!.City).SingleOrDefaultAsync(ct);
        var payload = BrCode.Build(key.Value, me.Name, city ?? "SAO PAULO", req.Amount, req.Description,
            txId: req.Amount is null ? null : Guid.NewGuid().ToString("N")[..25]);
        return new QrCodeResponse(payload, qrCodes.PngBase64(payload), PixKeys.Mask(key.Type, key.Value), req.Amount);
    }

    public async Task<DecodedBrCodeResponse> DecodeAsync(DecodeBrCodeRequest req, CancellationToken ct)
    {
        var code = BrCode.Parse(req.Payload);
        var recipient = await directory.FindAsync(code.Key, ct) ?? throw new NotFoundException("A chave deste código não foi encontrada.");
        return new DecodedBrCodeResponse(code.Amount, code.Description, recipient.ToResponse());
    }

    public async Task<PixLimitsResponse> GetLimitsAsync(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var limits = await payments.LimitsAsync(me.AccountId, me.Tier, ct);
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(limits, me, ct);
    }

    public async Task<PixLimitsResponse> ChangeLimitsAsync(ChangePixLimitsRequest req, CancellationToken ct)
    {
        await authorizer.VerifyPinAsync(req.Pin, ct);
        var me = await MeAsync(ct);
        var limits = await payments.LimitsAsync(me.AccountId, me.Tier, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var before = limits.Current(now);
        limits.Change(new LimitPair(req.DaytimeLimit, req.NighttimeLimit), me.Tier, now, TimeSpan.FromHours(options.LimitIncreaseGraceHours));
        audit.Record("pix.limits_changed", nameof(PixLimitSettings), me.AccountId, before,
            new { limits.DaytimeLimit, limits.NighttimeLimit, limits.PendingDaytimeLimit, limits.PendingNighttimeLimit, limits.PendingEffectiveAt });
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(limits, me, ct);
    }

    private async Task<PixLimitsResponse> ToResponseAsync(PixLimitSettings limits, Me me, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var current = limits.Current(now);
        var period = PixPeriod.For(now, BankTime.SaoPaulo);
        var used = await db.PixTransfers
            .Where(p => p.PayerAccountId == me.AccountId && p.CreatedAt >= period.StartUtc && p.CreatedAt < period.EndUtc)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var limit = period.IsNight ? current.Nighttime : current.Daytime;
        var max = PixLimitPolicy.Maximum(me.Tier);
        return new PixLimitsResponse(current.Daytime, current.Nighttime, limits.PendingDaytimeLimit, limits.PendingNighttimeLimit,
            limits.PendingEffectiveAt, max.Daytime, max.Nighttime, period.IsNight, used, Math.Max(0, limit - used));
    }

    private sealed record Me(Guid AccountId, string Name, AccountTier Tier);

    private async Task<Me> MeAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var me = await (
                from a in db.Accounts
                join u in db.Users on a.UserId equals u.Id
                join c in db.Customers on u.Id equals c.UserId
                where u.Id == userId
                select new { a.Id, Name = c.FullName ?? u.Name, a.Tier })
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException("Conta não encontrada.");
        return new Me(me.Id, me.Name, me.Tier);
    }

    private Task<string> NameOfAccountAsync(Guid accountId, CancellationToken ct) => (
            from a in db.Accounts
            join u in db.Users on a.UserId equals u.Id
            join c in db.Customers on u.Id equals c.UserId
            where a.Id == accountId
            select c.FullName ?? u.Name)
        .SingleAsync(ct);
}

/// <summary>PIX lines on receipts: end-to-end id, masked key and message.</summary>
public class PixReceiptDetails(IBankDbContext db) : IReceiptDetailsProvider
{
    public async Task AddDetailsAsync(Guid transactionId, IDictionary<string, string> details, CancellationToken ct)
    {
        var pix = await db.PixTransfers.AsNoTracking().SingleOrDefaultAsync(p => p.LedgerTransactionId == transactionId, ct);
        if (pix is not null)
        {
            details["ID da transação"] = pix.EndToEndId;
            if (pix.KeyType is { } type && pix.KeyValue is { } value) details["Chave PIX"] = PixKeys.Mask(type, value);
            if (!string.IsNullOrWhiteSpace(pix.Description)) details["Mensagem"] = pix.Description;
            return;
        }
        var refund = await (
                from r in db.PixTransfers.SelectMany(p => p.Refunds, (p, r) => new { r.LedgerTransactionId, r.EndToEndId, r.Reason, Original = p.EndToEndId })
                where r.LedgerTransactionId == transactionId
                select r)
            .SingleOrDefaultAsync(ct);
        if (refund is null) return;
        details["ID da devolução"] = refund.EndToEndId;
        details["PIX original"] = refund.Original;
        details["Motivo"] = refund.Reason;
    }
}
