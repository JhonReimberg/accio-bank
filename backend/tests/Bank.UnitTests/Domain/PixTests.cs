using Bank.Application.Common;
using Bank.Application.Pix;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Pix;

namespace Bank.UnitTests.Domain;

public class BrCodeTests
{
    [Fact]
    public void Crc16_matches_the_ccitt_false_check_value() => Assert.Equal(0x29B1, BrCode.Crc16("123456789"));

    [Fact]
    public void Build_and_parse_round_trip_with_amount_and_accents_removed()
    {
        var payload = BrCode.Build("ana@accio.dev", "Ana Conceição da Silva Gonçalves", "São Paulo", 25.5m, "Almoço", "PEDIDO42");

        Assert.StartsWith("000201010211", payload);
        Assert.Contains("5303986", payload);
        Assert.Contains("540525.50", payload);
        var data = BrCode.Parse(payload);
        Assert.Equal(("ana@accio.dev", 25.5m, "PEDIDO42", "Almoco"), (data.Key, data.Amount, data.TxId, data.Description));
        Assert.Equal("Ana Conceicao da Silva Go", data.MerchantName); // ASCII, 25 chars max
        Assert.Equal("Sao Paulo", data.MerchantCity);
    }

    [Fact]
    public void Static_code_without_amount_lets_the_payer_choose()
    {
        var data = BrCode.Parse(BrCode.Build("52998224725", "Ana", "Recife"));
        Assert.Null(data.Amount);
        Assert.Equal("***", data.TxId);
    }

    [Fact]
    public void Tampered_codes_fail_the_checksum()
    {
        var payload = BrCode.Build("ana@accio.dev", "Ana", "Recife", 10m);
        var tampered = payload.Replace("540510.00", "540599.00");
        Assert.Equal("brcode.checksum", Assert.Throws<DomainException>(() => BrCode.Parse(tampered)).Code);
        Assert.Equal("brcode.invalid", Assert.Throws<DomainException>(() => BrCode.Parse("not a pix code")).Code);
    }
}

public class PixKeyTests
{
    private const string OwnerCpf = "52998224725";
    private static readonly DateTime Now = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ana@accio.dev", PixKeyType.Email, "ana@accio.dev")]
    [InlineData(" Ana@Accio.dev ", PixKeyType.Email, "ana@accio.dev")]
    [InlineData("+55 (11) 91234-5678", PixKeyType.Phone, "+5511912345678")]
    [InlineData("529.982.247-25", PixKeyType.Cpf, "52998224725")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", PixKeyType.Random, "0f8fad5b-d9cb-469f-a165-70867728950e")]
    public void Keys_are_detected_and_normalized(string input, PixKeyType type, string value) =>
        Assert.Equal((type, value), PixKeys.Detect(input));

    [Theory]
    [InlineData("11912345678")] // without +55 it is read as a CPF, and it is not a valid one
    [InlineData("nao-e-chave")]
    public void Unrecognized_input_is_not_a_key(string input) => Assert.Null(PixKeys.Detect(input));

    [Fact]
    public void Cpf_key_must_belong_to_the_account_holder()
    {
        var ex = Assert.Throws<DomainException>(() => PixKey.Register(Guid.NewGuid(), PixKeyType.Cpf, "111.444.777-35", OwnerCpf, 0, Now));
        Assert.Equal("pix.key_not_owner", ex.Code);
        PixKey.Register(Guid.NewGuid(), PixKeyType.Cpf, OwnerCpf, OwnerCpf, 0, Now);
    }

    [Fact]
    public void At_most_five_keys_per_account() =>
        Assert.Equal("pix.key_limit",
            Assert.Throws<DomainException>(() => PixKey.Register(Guid.NewGuid(), PixKeyType.Random, null, OwnerCpf, 5, Now)).Code);

    [Fact]
    public void Masks_hide_most_of_the_key() =>
        Assert.Equal(("***.982.247-**", "an***@accio.dev", "+5511*****5678"),
            (PixKeys.Mask(PixKeyType.Cpf, OwnerCpf), PixKeys.Mask(PixKeyType.Email, "ana@accio.dev"), PixKeys.Mask(PixKeyType.Phone, "+5511912345678")));
}

public class PixLimitTests
{
    private static DateTime Brasilia(int hour, int minute = 0) => BankTime.ToUtc(new DateTime(2026, 11, 16, hour, minute, 0));

    [Theory]
    [InlineData(5, 59, true)]
    [InlineData(6, 0, false)]
    [InlineData(19, 59, false)]
    [InlineData(20, 0, true)]
    [InlineData(23, 30, true)]
    public void Night_runs_from_20h_to_6h_brasilia_time(int hour, int minute, bool night) =>
        Assert.Equal(night, PixPeriod.For(Brasilia(hour, minute), BankTime.SaoPaulo).IsNight);

    [Fact]
    public void Early_morning_belongs_to_the_night_that_started_the_day_before()
    {
        var period = PixPeriod.For(Brasilia(2), BankTime.SaoPaulo);
        Assert.Equal(BankTime.ToUtc(new DateTime(2026, 11, 15, 20, 0, 0)), period.StartUtc);
        Assert.Equal(BankTime.ToUtc(new DateTime(2026, 11, 16, 6, 0, 0)), period.EndUtc);
    }

    [Fact]
    public void Lowering_is_immediate_raising_waits_for_the_grace_period()
    {
        var now = Brasilia(10);
        var limits = PixLimitSettings.ForTier(Guid.NewGuid(), AccountTier.Basic); // 5000 / 1000
        limits.Change(new LimitPair(3_000, 1_500), AccountTier.Basic, now, TimeSpan.FromHours(24));

        Assert.Equal(new LimitPair(3_000, 1_000), limits.Current(now));
        Assert.Equal(1_500m, limits.PendingNighttimeLimit);
        Assert.Equal(new LimitPair(3_000, 1_000), limits.Current(now.AddHours(23)));
        Assert.Equal(new LimitPair(3_000, 1_500), limits.Current(now.AddHours(24)));
        Assert.Null(limits.PendingEffectiveAt);
    }

    [Fact]
    public void Night_limit_cannot_exceed_day_limit_nor_the_tier_maximum()
    {
        var limits = PixLimitSettings.ForTier(Guid.NewGuid(), AccountTier.Basic);
        var now = Brasilia(10);
        Assert.Equal("pix.limit_night_above_day", Assert.Throws<DomainException>(() =>
            limits.Change(new LimitPair(1_000, 2_000), AccountTier.Basic, now, TimeSpan.Zero)).Code);
        Assert.Equal("pix.limit_above_tier", Assert.Throws<DomainException>(() =>
            limits.Change(new LimitPair(10_000.01m, 1_000), AccountTier.Basic, now, TimeSpan.Zero)).Code);
    }

    [Fact]
    public void Limit_is_a_total_per_period()
    {
        var limits = PixLimitSettings.ForTier(Guid.NewGuid(), AccountTier.Basic);
        var day = PixPeriod.For(Brasilia(10), BankTime.SaoPaulo);
        limits.EnsureWithinLimit(day, alreadySentInPeriod: 4_000, amount: 1_000, Brasilia(10));
        Assert.Equal("pix.day_limit", Assert.Throws<DomainException>(() =>
            limits.EnsureWithinLimit(day, 4_000, 1_000.01m, Brasilia(10))).Code);

        var night = PixPeriod.For(Brasilia(22), BankTime.SaoPaulo);
        Assert.Equal("pix.night_limit", Assert.Throws<DomainException>(() =>
            limits.EnsureWithinLimit(night, 0, 1_000.01m, Brasilia(22))).Code);
    }
}

public class PixTransferTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);

    private static PixTransfer Pix(decimal amount = 30) =>
        PixTransfer.Record(Guid.NewGuid(), Guid.NewGuid(), amount, null, PixKeyType.Email, "a@b.dev", Guid.NewGuid(), Now, new Random(1));

    [Fact]
    public void End_to_end_id_has_the_network_format()
    {
        var pix = Pix();
        Assert.Matches("^E99999999202611161500[A-Z0-9]{11}$", pix.EndToEndId);
        Assert.Equal(32, pix.EndToEndId.Length);
    }

    [Fact]
    public void Partial_refunds_add_up_to_at_most_the_original_amount()
    {
        var pix = Pix(30);
        var refund = pix.Refund(10, "Valor errado", Guid.NewGuid(), Now, new Random(2));
        Assert.StartsWith("D", refund.EndToEndId);
        Assert.Equal("pix.refund_exceeds", Assert.Throws<DomainException>(() => pix.Refund(20.01m, "x", Guid.NewGuid(), Now, new Random())).Code);
        pix.Refund(20, "Resto", Guid.NewGuid(), Now, new Random());
        Assert.Equal(0m, pix.RefundableAmount);
    }

    [Fact]
    public void Refund_window_is_90_days() =>
        Assert.Equal("pix.refund_expired", Assert.Throws<DomainException>(() =>
            Pix().Refund(1, "x", Guid.NewGuid(), Now.AddDays(90).AddMinutes(1), new Random())).Code);
}

public class PixScheduleTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 1, 10);

    private static PixSchedule Monthly(DateOnly first, DateOnly? end = null) =>
        PixSchedule.Create(Guid.NewGuid(), PixKeyType.Email, "a@b.dev", 100, null, ScheduleFrequency.Monthly, first, end, Today, Now);

    [Fact]
    public void Monthly_keeps_the_original_day_when_the_month_allows()
    {
        var schedule = Monthly(new DateOnly(2026, 1, 31));
        schedule.Advance(null);
        Assert.Equal(new DateOnly(2026, 2, 28), schedule.NextRunDate);
        schedule.Advance(null);
        Assert.Equal(new DateOnly(2026, 3, 31), schedule.NextRunDate);
    }

    [Fact]
    public void Completes_after_the_end_date_or_after_a_single_run()
    {
        var recurring = Monthly(new DateOnly(2026, 1, 15), end: new DateOnly(2026, 2, 20));
        recurring.Advance(null);
        recurring.Advance(null);
        Assert.Equal(ScheduleStatus.Completed, recurring.Status);

        var once = PixSchedule.Create(Guid.NewGuid(), PixKeyType.Email, "a@b.dev", 50, null, ScheduleFrequency.Once, Today, null, Today, Now);
        once.Advance(null);
        Assert.Equal(ScheduleStatus.Completed, once.Status);
    }

    [Fact]
    public void A_failed_run_is_recorded_and_raises_an_alert_but_the_schedule_moves_on()
    {
        var schedule = PixSchedule.Create(Guid.NewGuid(), PixKeyType.Email, "a@b.dev", 50, null, ScheduleFrequency.Weekly, Today, null, Today, Now);
        schedule.Advance("Saldo insuficiente.");
        Assert.Equal(("Saldo insuficiente.", Today.AddDays(7)), (schedule.LastError, schedule.NextRunDate));
        Assert.Single(schedule.DomainEvents.OfType<ScheduledPixFailed>());
    }

    [Fact]
    public void Cannot_schedule_in_the_past() =>
        Assert.Equal("pix.schedule_past", Assert.Throws<DomainException>(() => Monthly(Today.AddDays(-1))).Code);
}
