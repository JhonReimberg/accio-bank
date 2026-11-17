using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Pix;

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleFrequency>))]
public enum ScheduleFrequency
{
    Once,
    Weekly,
    Monthly,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleStatus>))]
public enum ScheduleStatus
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>A scheduled (once) or recurring PIX, executed by a background job on its dates.</summary>
public class PixSchedule : Entity
{
    private PixSchedule() { }

    public Guid PayerAccountId { get; private set; }
    public PixKeyType KeyType { get; private set; }
    public string KeyValue { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
    public ScheduleFrequency Frequency { get; private set; }
    public DateOnly FirstDate { get; private set; }
    /// <summary>Brasília date of the next execution.</summary>
    public DateOnly NextRunDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public ScheduleStatus Status { get; private set; }
    public int Executions { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static PixSchedule Create(
        Guid payerAccountId, PixKeyType keyType, string keyValue, decimal amount, string? description,
        ScheduleFrequency frequency, DateOnly firstDate, DateOnly? endDate, DateOnly today, DateTime now)
    {
        Money.EnsureValidAmount(amount);
        if (firstDate < today)
            throw new DomainException("pix.schedule_past", "A data do agendamento não pode estar no passado.");
        if (firstDate > today.AddYears(1))
            throw new DomainException("pix.schedule_too_far", "Agende para no máximo 1 ano à frente.");
        if (frequency == ScheduleFrequency.Once && endDate is not null)
            throw new DomainException("pix.schedule_end_date", "Agendamentos únicos não têm data final.");
        if (endDate < firstDate)
            throw new DomainException("pix.schedule_end_date", "A data final deve ser depois da primeira execução.");

        return new PixSchedule
        {
            PayerAccountId = payerAccountId,
            KeyType = keyType,
            KeyValue = keyValue,
            Amount = amount,
            Description = description,
            Frequency = frequency,
            FirstDate = firstDate,
            NextRunDate = firstDate,
            EndDate = endDate,
            Status = ScheduleStatus.Active,
            CreatedAt = now,
        };
    }

    public bool IsDue(DateOnly today) => Status == ScheduleStatus.Active && NextRunDate <= today;

    /// <summary>Moves to the next date after an execution attempt (successful or not: a failed PIX is not retried).</summary>
    public void Advance(string? error)
    {
        Executions++;
        LastError = error;
        if (error is not null) Raise(new ScheduledPixFailed(Id, PayerAccountId, Amount, NextRunDate, error));

        DateOnly? next = Frequency switch
        {
            ScheduleFrequency.Weekly => NextRunDate.AddDays(7),
            // Keeps the original day of month when possible (31st -> 30th/28th -> 31st again).
            ScheduleFrequency.Monthly => MonthlyAfter(NextRunDate),
            _ => null,
        };
        if (next is null || (EndDate is { } end && next > end)) Status = ScheduleStatus.Completed;
        else NextRunDate = next.Value;
    }

    public void Cancel()
    {
        if (Status != ScheduleStatus.Active)
            throw new DomainException("pix.schedule_not_active", "Este agendamento não está ativo.");
        Status = ScheduleStatus.Cancelled;
    }

    private DateOnly MonthlyAfter(DateOnly current)
    {
        var month = current.AddMonths(1);
        var day = Math.Min(FirstDate.Day, DateTime.DaysInMonth(month.Year, month.Month));
        return new DateOnly(month.Year, month.Month, day);
    }
}

public sealed record ScheduledPixFailed(Guid ScheduleId, Guid PayerAccountId, decimal Amount, DateOnly Date, string Reason) : IDomainEvent;

/// <summary>A saved recipient (favorite contact).</summary>
public class PixContact : Entity
{
    private PixContact() { }

    public Guid OwnerUserId { get; private set; }
    public string Nickname { get; private set; } = "";
    public PixKeyType KeyType { get; private set; }
    public string KeyValue { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    public static PixContact Create(Guid ownerUserId, string nickname, PixKeyType keyType, string keyValue, DateTime now) =>
        new() { OwnerUserId = ownerUserId, Nickname = nickname.Trim(), KeyType = keyType, KeyValue = keyValue, CreatedAt = now };
}
