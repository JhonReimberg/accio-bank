using Bank.Domain.Common;

namespace Bank.Domain.Pix;

/// <summary>A PIX payment between two accounts, with its refunds (devoluções).</summary>
public class PixTransfer : Entity
{
    /// <summary>Refunds can be requested up to 90 days after the payment.</summary>
    public static readonly TimeSpan RefundWindow = TimeSpan.FromDays(90);

    private readonly List<PixRefund> _refunds = [];

    private PixTransfer() { }

    /// <summary>32-char end-to-end id, as in the PIX network (E + ISPB + timestamp + random).</summary>
    public string EndToEndId { get; private set; } = "";
    public Guid PayerAccountId { get; private set; }
    public Guid PayeeAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
    public PixKeyType? KeyType { get; private set; }
    public string? KeyValue { get; private set; }
    public Guid LedgerTransactionId { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public Guid? ScheduleId { get; private set; }
    /// <summary>Which occurrence of a schedule this is; unique with ScheduleId so a job rerun cannot pay twice.</summary>
    public DateOnly? ScheduleOccurrence { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<PixRefund> Refunds => _refunds;

    public decimal RefundableAmount => Amount - RefundedAmount;

    public static PixTransfer Record(
        Guid payerAccountId, Guid payeeAccountId, decimal amount, string? description, PixKeyType? keyType, string? keyValue,
        Guid ledgerTransactionId, DateTime now, Random random, Guid? scheduleId = null, DateOnly? occurrence = null) => new()
    {
        EndToEndId = EndToEnd.New('E', now, random),
        PayerAccountId = payerAccountId,
        PayeeAccountId = payeeAccountId,
        Amount = amount,
        Description = description,
        KeyType = keyType,
        KeyValue = keyValue,
        LedgerTransactionId = ledgerTransactionId,
        ScheduleId = scheduleId,
        ScheduleOccurrence = occurrence,
        CreatedAt = now,
    };

    /// <summary>Validates and registers a full or partial refund; the money itself moves through the ledger.</summary>
    public PixRefund Refund(decimal amount, string reason, Guid ledgerTransactionId, DateTime now, Random random)
    {
        Money.EnsureValidAmount(amount);
        if (now - CreatedAt > RefundWindow)
            throw new DomainException("pix.refund_expired", "O prazo de 90 dias para devolução deste PIX terminou.");
        if (amount > RefundableAmount)
            throw new DomainException("pix.refund_exceeds", $"Só é possível devolver até {RefundableAmount:N2}.");
        RefundedAmount += amount;
        var refund = PixRefund.Create(Id, amount, reason, ledgerTransactionId, EndToEnd.New('D', now, random), now);
        _refunds.Add(refund);
        return refund;
    }
}

public class PixRefund : Entity
{
    private PixRefund() { }

    public Guid PixTransferId { get; private set; }
    public string EndToEndId { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = "";
    public Guid LedgerTransactionId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    internal static PixRefund Create(Guid pixTransferId, decimal amount, string reason, Guid ledgerTransactionId, string endToEndId, DateTime now) => new()
    {
        PixTransferId = pixTransferId,
        EndToEndId = endToEndId,
        Amount = amount,
        Reason = reason,
        LedgerTransactionId = ledgerTransactionId,
        CreatedAt = now,
    };
}

internal static class EndToEnd
{
    /// <summary>Fictitious ISPB of this bank.</summary>
    private const string Ispb = "99999999";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string New(char prefix, DateTime now, Random random) =>
        $"{prefix}{Ispb}{now:yyyyMMddHHmm}" + string.Concat(Enumerable.Range(0, 11).Select(_ => Alphabet[random.Next(Alphabet.Length)]));
}
