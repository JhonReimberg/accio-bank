using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Boletos;

public sealed record BoletoData(string Barcode, string BankCode, decimal Amount, DateOnly? DueDate);

/// <summary>
/// FEBRABAN "boleto de cobrança": 44-digit barcode and 47-digit linha digitável (three fields with mod-10 check
/// digits, the general mod-11 check digit, due-date factor and amount). Issued boletos use a fictitious bank code.
/// </summary>
public static class BoletoCode
{
    public const string VelaBankCode = "999";
    private static readonly DateOnly FactorBase = new(1997, 10, 7);
    private static readonly DomainException Invalid = new("boleto.invalid", "Código do boleto inválido. Confira os números digitados.");

    public static string Barcode(string freeField, decimal amount, DateOnly due)
    {
        if (freeField.Length != 25 || !freeField.All(char.IsAsciiDigit)) throw new ArgumentException("Free field must have 25 digits.", nameof(freeField));
        var cents = (long)(Money.Round(amount) * 100);
        var noDv = $"{VelaBankCode}9{Factor(due):D4}{cents:D10}{freeField}";
        return noDv[..4] + Mod11(noDv) + noDv[4..];
    }

    public static string DigitableLine(string barcode)
    {
        static string WithDv(string s) => s + Mod10(s);
        var free = barcode[19..];
        return WithDv(barcode[..4] + free[..5]) + WithDv(free[5..15]) + WithDv(free[15..]) + barcode[4] + barcode[5..19];
    }

    /// <summary>"99999.99999 99999.999999 99999.999999 9 99999999999999", as printed on the boleto.</summary>
    public static string Format(string line) =>
        $"{line[..5]}.{line[5..10]} {line[10..15]}.{line[15..21]} {line[21..26]}.{line[26..32]} {line[32]} {line[33..]}";

    /// <summary>Accepts the linha digitável (47 digits) or the barcode (44), with or without punctuation.</summary>
    public static BoletoData Parse(string input, DateOnly today)
    {
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        string barcode;
        if (digits.Length == 47)
        {
            if (Mod10(digits[..9]) != digits[9] - '0' || Mod10(digits[10..20]) != digits[20] - '0' || Mod10(digits[21..31]) != digits[31] - '0')
                throw Invalid;
            barcode = digits[..4] + digits[32] + digits[33..] + digits[4..9] + digits[10..20] + digits[21..31];
        }
        else if (digits.Length == 44) barcode = digits;
        else throw Invalid;

        if (Mod11(barcode[..4] + barcode[5..]) != barcode[4] - '0') throw Invalid;
        var factor = int.Parse(barcode[5..9]);
        var amount = long.Parse(barcode[9..19]) / 100m;
        return new BoletoData(barcode, barcode[..3], amount, factor == 0 ? null : DueDate(factor, today));
    }

    /// <summary>Days since 1997-10-07; it went past 9999 on 2025-02-22 and restarted at 1000.</summary>
    public static int Factor(DateOnly due)
    {
        var days = due.DayNumber - FactorBase.DayNumber;
        if (days < 1000) throw new ArgumentOutOfRangeException(nameof(due));
        return days <= 9999 ? days : (days - 10_000) % 9_000 + 1_000;
    }

    /// <summary>The factor repeats every 9,000 days: the date closest to today is the right one.</summary>
    private static DateOnly DueDate(int factor, DateOnly today) =>
        Enumerable.Range(0, 4).Select(k => FactorBase.AddDays(factor + 9_000 * k))
            .MinBy(d => Math.Abs(d.DayNumber - today.DayNumber));

    /// <summary>Weights 2,1,2,1... from the right; products above 9 add their digits.</summary>
    public static int Mod10(string s)
    {
        var sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var p = (s[^(i + 1)] - '0') * (i % 2 == 0 ? 2 : 1);
            sum += p > 9 ? p - 9 : p;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>Weights 2..9 from the right; results 0, 10 and 11 become 1.</summary>
    public static int Mod11(string s)
    {
        var sum = 0;
        for (var i = 0; i < s.Length; i++) sum += (s[^(i + 1)] - '0') * (i % 8 + 2);
        var dv = 11 - sum % 11;
        return dv is 0 or 10 or 11 ? 1 : dv;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<BoletoStatus>))]
public enum BoletoStatus { Open, Paid, Cancelled }

/// <summary>A boleto issued by a customer to receive money.</summary>
public class Boleto : Entity
{
    private Boleto() { }

    public Guid BeneficiaryAccountId { get; private set; }
    public string BeneficiaryName { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Description { get; private set; }
    public string Barcode { get; private set; } = "";
    public string DigitableLine { get; private set; } = "";
    public BoletoStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    public static Boleto Issue(Guid accountId, string beneficiaryName, decimal amount, DateOnly due, string? description, DateOnly today, DateTime now, Random random)
    {
        Money.EnsureValidAmount(amount);
        if (amount > 1_000_000) throw new DomainException("boleto.amount", $"O valor máximo do boleto é {Money.Brl(1_000_000)}.");
        if (due < today || due > today.AddYears(1)) throw new DomainException("boleto.due_date", "O vencimento deve ser entre hoje e um ano.");
        var barcode = BoletoCode.Barcode(string.Concat(Enumerable.Range(0, 25).Select(_ => random.Next(10))), amount, due);
        return new Boleto
        {
            BeneficiaryAccountId = accountId, BeneficiaryName = beneficiaryName, Amount = amount, DueDate = due, Description = description,
            Barcode = barcode, DigitableLine = BoletoCode.DigitableLine(barcode), Status = BoletoStatus.Open, CreatedAt = now,
        };
    }

    public void MarkPaid(DateTime now)
    {
        if (Status != BoletoStatus.Open) throw new DomainException("boleto.not_open", Status == BoletoStatus.Paid ? "Este boleto já foi pago." : "Este boleto foi cancelado.");
        (Status, PaidAt) = (BoletoStatus.Paid, now);
    }

    public void Cancel()
    {
        if (Status != BoletoStatus.Open) throw new DomainException("boleto.not_open", "Só boletos em aberto podem ser cancelados.");
        Status = BoletoStatus.Cancelled;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<BoletoPaymentStatus>))]
public enum BoletoPaymentStatus { Scheduled, Paid, Failed, Cancelled }

/// <summary>A boleto paid (or scheduled to be paid) by a customer, ours or from another bank.</summary>
public class BoletoPayment : Entity
{
    private BoletoPayment() { }

    public Guid PayerAccountId { get; private set; }
    public string Barcode { get; private set; } = "";
    public string BeneficiaryName { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateOnly ScheduledFor { get; private set; }
    public BoletoPaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid? LedgerTransactionId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    public static BoletoPayment Create(Guid payerAccountId, BoletoData data, string beneficiaryName, decimal amount, DateOnly scheduledFor, DateTime now) =>
        new()
        {
            PayerAccountId = payerAccountId, Barcode = data.Barcode, BeneficiaryName = beneficiaryName, Amount = amount, DueDate = data.DueDate,
            ScheduledFor = scheduledFor, Status = BoletoPaymentStatus.Scheduled, CreatedAt = now,
        };

    public void MarkPaid(Guid transactionId, DateTime now) => (Status, LedgerTransactionId, PaidAt) = (BoletoPaymentStatus.Paid, transactionId, now);

    public void Fail(string reason)
    {
        (Status, FailureReason) = (BoletoPaymentStatus.Failed, reason);
        Raise(new BoletoPaymentFailed(Id, PayerAccountId, Amount, BeneficiaryName, reason));
    }

    public void Cancel()
    {
        if (Status != BoletoPaymentStatus.Scheduled) throw new DomainException("boleto.not_scheduled", "Só pagamentos agendados podem ser cancelados.");
        Status = BoletoPaymentStatus.Cancelled;
    }
}

public sealed record BoletoPaymentFailed(Guid PaymentId, Guid PayerAccountId, decimal Amount, string BeneficiaryName, string Reason) : IDomainEvent;
