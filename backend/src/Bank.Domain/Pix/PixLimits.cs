using Bank.Domain.Accounts;
using Bank.Domain.Common;

namespace Bank.Domain.Pix;

public sealed record LimitPair(decimal Daytime, decimal Nighttime);

/// <summary>Default and maximum PIX limits per account tier.</summary>
public static class PixLimitPolicy
{
    public static LimitPair Default(AccountTier tier) => tier switch
    {
        AccountTier.Basic => new(5_000, 1_000),
        AccountTier.Plus => new(20_000, 2_000),
        _ => new(50_000, 5_000),
    };

    public static LimitPair Maximum(AccountTier tier) => tier switch
    {
        AccountTier.Basic => new(10_000, 2_000),
        AccountTier.Plus => new(50_000, 10_000),
        _ => new(200_000, 20_000),
    };
}

/// <summary>
/// Daytime (06:00–20:00) and nighttime (20:00–06:00) PIX limits of an account, as totals per period.
/// Lowering a limit is immediate; raising it only takes effect after a grace period (anti-fraud rule:
/// someone who steals the phone cannot raise the limit and empty the account right away).
/// </summary>
public class PixLimitSettings
{
    private PixLimitSettings() { }

    public Guid AccountId { get; private set; }
    public decimal DaytimeLimit { get; private set; }
    public decimal NighttimeLimit { get; private set; }
    public decimal? PendingDaytimeLimit { get; private set; }
    public decimal? PendingNighttimeLimit { get; private set; }
    public DateTime? PendingEffectiveAt { get; private set; }

    public static PixLimitSettings ForTier(Guid accountId, AccountTier tier)
    {
        var defaults = PixLimitPolicy.Default(tier);
        return new PixLimitSettings { AccountId = accountId, DaytimeLimit = defaults.Daytime, NighttimeLimit = defaults.Nighttime };
    }

    /// <summary>Limits in force at <paramref name="now"/>, applying a pending increase whose grace period ended.</summary>
    public LimitPair Current(DateTime now)
    {
        if (PendingEffectiveAt is { } at && at <= now)
        {
            DaytimeLimit = PendingDaytimeLimit ?? DaytimeLimit;
            NighttimeLimit = PendingNighttimeLimit ?? NighttimeLimit;
            PendingDaytimeLimit = PendingNighttimeLimit = null;
            PendingEffectiveAt = null;
        }
        return new LimitPair(DaytimeLimit, NighttimeLimit);
    }

    public void Change(LimitPair requested, AccountTier tier, DateTime now, TimeSpan gracePeriod)
    {
        Money.EnsureValidAmount(requested.Daytime);
        Money.EnsureValidAmount(requested.Nighttime);
        if (requested.Nighttime > requested.Daytime)
            throw new DomainException("pix.limit_night_above_day", "O limite noturno não pode ser maior que o diurno.");
        var max = PixLimitPolicy.Maximum(tier);
        if (requested.Daytime > max.Daytime || requested.Nighttime > max.Nighttime)
            throw new DomainException("pix.limit_above_tier",
                $"O máximo para sua conta é {Money.Brl(max.Daytime)} de dia e {Money.Brl(max.Nighttime)} à noite.");

        var current = Current(now);
        // Decreases apply now; increases wait. A new request replaces any pending one.
        PendingDaytimeLimit = PendingNighttimeLimit = null;
        PendingEffectiveAt = null;
        if (requested.Daytime <= current.Daytime) DaytimeLimit = requested.Daytime;
        else PendingDaytimeLimit = requested.Daytime;
        if (requested.Nighttime <= current.Nighttime) NighttimeLimit = requested.Nighttime;
        else PendingNighttimeLimit = requested.Nighttime;
        if (PendingDaytimeLimit is not null || PendingNighttimeLimit is not null) PendingEffectiveAt = now + gracePeriod;
    }

    public void EnsureWithinLimit(PixPeriod period, decimal alreadySentInPeriod, decimal amount, DateTime now)
    {
        var limits = Current(now);
        var limit = period.IsNight ? limits.Nighttime : limits.Daytime;
        if (alreadySentInPeriod + amount > limit)
            throw new DomainException(period.IsNight ? "pix.night_limit" : "pix.day_limit",
                $"Valor acima do seu limite {(period.IsNight ? "noturno" : "diurno")}. Disponível agora: {Money.Brl(Math.Max(0, limit - alreadySentInPeriod))}.");
    }
}

/// <summary>The limit window a moment falls in, in UTC, computed on Brasília local time.</summary>
public sealed record PixPeriod(bool IsNight, DateTime StartUtc, DateTime EndUtc)
{
    public const int NightStartsAt = 20;
    public const int DayStartsAt = 6;

    public static PixPeriod For(DateTime utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
        var day = local.Date;
        DateTime start, end;
        bool night;
        if (local.Hour >= DayStartsAt && local.Hour < NightStartsAt)
        {
            (night, start, end) = (false, day.AddHours(DayStartsAt), day.AddHours(NightStartsAt));
        }
        else if (local.Hour >= NightStartsAt)
        {
            (night, start, end) = (true, day.AddHours(NightStartsAt), day.AddDays(1).AddHours(DayStartsAt));
        }
        else
        {
            (night, start, end) = (true, day.AddDays(-1).AddHours(NightStartsAt), day.AddHours(DayStartsAt));
        }
        return new PixPeriod(night, TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeZoneInfo.ConvertTimeToUtc(end, zone));
    }
}
