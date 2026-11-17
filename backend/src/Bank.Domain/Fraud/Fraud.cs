using System.Text.Json.Serialization;
using Bank.Domain.Common;
using Bank.Domain.Pix;

namespace Bank.Domain.Fraud;

[JsonConverter(typeof(JsonStringEnumConverter<FraudReviewStatus>))]
public enum FraudReviewStatus { Pending, Approved, Rejected }

/// <summary>What the rules look at for one outgoing payment.</summary>
public sealed record FraudSignals(decimal Amount, decimal AverageOutgoing90Days, int PaymentsLast10Minutes, bool NewDevice);

/// <summary>Anti-fraud thresholds, edited by the Admin. A single row.</summary>
public class FraudPolicy
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public bool Enabled { get; private set; } = true;
    /// <summary>Flag when the amount is this many times the customer's average outgoing payment...</summary>
    public decimal AverageMultiplier { get; private set; } = 5m;
    /// <summary>...and at least this much (small amounts are never held for this rule).</summary>
    public decimal AverageRuleMinAmount { get; private set; } = 1_000m;
    /// <summary>A device first seen less than 24h ago paying at least this much.</summary>
    public decimal NewDeviceAmount { get; private set; } = 2_000m;
    /// <summary>This many payments in 10 minutes (counting the current one).</summary>
    public int VelocityCount { get; private set; } = 5;

    public IReadOnlyList<string> Evaluate(FraudSignals s)
    {
        if (!Enabled) return [];
        var reasons = new List<string>();
        if (s.Amount >= AverageRuleMinAmount && s.AverageOutgoing90Days > 0 && s.Amount > s.AverageOutgoing90Days * AverageMultiplier)
            reasons.Add($"Valor {Money.Brl(s.Amount)} acima de {AverageMultiplier}x a média do cliente ({Money.Brl(s.AverageOutgoing90Days)})");
        if (s.NewDevice && s.Amount >= NewDeviceAmount)
            reasons.Add($"Dispositivo novo com valor alto ({Money.Brl(s.Amount)})");
        if (s.PaymentsLast10Minutes + 1 >= VelocityCount)
            reasons.Add($"{s.PaymentsLast10Minutes + 1} pagamentos em 10 minutos");
        return reasons;
    }

    public void Update(bool enabled, decimal averageMultiplier, decimal averageRuleMinAmount, decimal newDeviceAmount, int velocityCount)
    {
        if (averageMultiplier < 1 || averageRuleMinAmount < 0 || newDeviceAmount < 0 || velocityCount < 2)
            throw new DomainException("fraud_policy.invalid", "Parâmetros antifraude inválidos.");
        (Enabled, AverageMultiplier, AverageRuleMinAmount, NewDeviceAmount, VelocityCount) =
            (enabled, averageMultiplier, averageRuleMinAmount, newDeviceAmount, velocityCount);
    }
}

/// <summary>A payment held for analysis: its amount stays blocked on the payer's balance until an analyst decides.</summary>
public class FraudReview : Entity
{
    private FraudReview() { }

    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid PayeeAccountId { get; private set; }
    public string PayeeName { get; private set; } = "";
    public PixKeyType KeyType { get; private set; }
    public string KeyValue { get; private set; } = "";
    public string? Description { get; private set; }
    public Guid HoldId { get; private set; }
    public string Reasons { get; private set; } = "";
    public FraudReviewStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public Guid? PixTransferId { get; private set; }

    public static FraudReview Hold(Guid accountId, Guid userId, decimal amount, Guid payeeAccountId, string payeeName, PixKeyType keyType,
        string keyValue, string? description, Guid holdId, IReadOnlyList<string> reasons, DateTime now)
    {
        var review = new FraudReview
        {
            AccountId = accountId, UserId = userId, Amount = amount, PayeeAccountId = payeeAccountId, PayeeName = payeeName,
            KeyType = keyType, KeyValue = keyValue, Description = description, HoldId = holdId,
            Reasons = string.Join("; ", reasons), Status = FraudReviewStatus.Pending, CreatedAt = now,
        };
        review.Raise(new FraudReviewChanged(review.Id, userId, amount, payeeName, FraudReviewStatus.Pending));
        return review;
    }

    public void Approve(Guid reviewerId, Guid pixTransferId, DateTime now)
    {
        Decide(reviewerId, FraudReviewStatus.Approved, now);
        PixTransferId = pixTransferId;
    }

    public void Reject(Guid reviewerId, DateTime now) => Decide(reviewerId, FraudReviewStatus.Rejected, now);

    private void Decide(Guid reviewerId, FraudReviewStatus status, DateTime now)
    {
        if (Status != FraudReviewStatus.Pending) throw new DomainException("fraud.decided", "Esta análise já foi concluída.");
        (Status, ReviewedBy, ReviewedAt) = (status, reviewerId, now);
        Raise(new FraudReviewChanged(Id, UserId, Amount, PayeeName, status));
    }
}

public sealed record FraudReviewChanged(Guid ReviewId, Guid UserId, decimal Amount, string PayeeName, FraudReviewStatus Status) : IDomainEvent;
