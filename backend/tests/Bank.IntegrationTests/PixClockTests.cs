using System.Net;
using System.Net.Http.Json;
using Bank.Application.Common;
using Bank.Application.Notifications;
using Bank.Application.Pix;
using Bank.Domain.Pix;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bank.IntegrationTests;

/// <summary>Business clock under test control: starts on a Monday, 10:00 in Brasília.</summary>
public class FakeClockFactory : BankApiFactory
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(BankTime.ToUtc(new DateTime(2026, 11, 16, 10, 0, 0)), TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock));
    }

    public void SetBrasiliaTime(DateTime local) => Clock.SetUtcNow(new DateTimeOffset(BankTime.ToUtc(local), TimeSpan.Zero));

    public async Task RunScheduledPixJobAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ExecuteScheduledPixJob>().RunAsync(CancellationToken.None);
    }
}

/// <summary>Night limit, grace period and schedules depend on the time of day, so these tests share a fake clock (run in order).</summary>
public class PixClockTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    [Fact]
    public async Task Limits_daytime_total_then_lower_night_limit_and_grace_period_for_increases()
    {
        factory.SetBrasiliaTime(new DateTime(2026, 11, 16, 10, 0, 0));
        var payer = await TestUsers.CreateAsync(factory, deposit: 10_000);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);

        // Basic tier: 5,000 per daytime period, 1,000 per night.
        Assert.Equal(HttpStatusCode.OK, (await payer.Client.PixAsync(payee.Email, 4_000)).StatusCode);
        Assert.Equal("pix.day_limit", await PixClient.CodeOf(await payer.Client.PixAsync(payee.Email, 1_000.01m)));

        factory.SetBrasiliaTime(new DateTime(2026, 11, 16, 21, 0, 0));
        Assert.Equal(HttpStatusCode.OK, (await payer.Client.PixAsync(payee.Email, 1_000)).StatusCode);
        Assert.Equal("pix.night_limit", await PixClient.CodeOf(await payer.Client.PixAsync(payee.Email, 0.01m)));

        // Raising the night limit waits 24h; lowering the day limit is immediate.
        var changed = await (await payer.Client.PutAsJsonAsync("/api/pix/limits", new ChangePixLimitsRequest(3_000, 2_000, TestUsers.Pin)))
            .Content.ReadFromJsonAsync<PixLimitsResponse>();
        Assert.Equal((3_000m, 1_000m, 2_000m), (changed!.DaytimeLimit, changed.NighttimeLimit, changed.PendingNighttimeLimit));

        factory.SetBrasiliaTime(new DateTime(2026, 11, 17, 21, 1, 0));
        var later = await payer.Client.GetFromJsonAsync<PixLimitsResponse>("/api/pix/limits");
        Assert.Equal((2_000m, (decimal?)null, true), (later!.NighttimeLimit, later.PendingNighttimeLimit, later.IsNightNow));
        Assert.Equal(HttpStatusCode.OK, (await payer.Client.PixAsync(payee.Email, 1_500)).StatusCode);
    }

    [Fact]
    public async Task Scheduled_monthly_pix_runs_once_per_date_and_failures_are_reported()
    {
        factory.SetBrasiliaTime(new DateTime(2026, 12, 10, 9, 0, 0));
        var payer = await TestUsers.CreateAsync(factory, deposit: 150);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);

        var create = await payer.Client.PostIdempotentAsync("/api/pix/schedules", new CreatePixScheduleRequest(
            payee.Email, 100, "Aluguel", new DateOnly(2026, 12, 10), ScheduleFrequency.Monthly, null, TestUsers.Pin));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        await factory.RunScheduledPixJobAsync();
        await factory.RunScheduledPixJobAsync(); // a rerun on the same day must not pay twice
        Assert.Equal(100m, (await payee.Client.AccountAsync()).AvailableBalance);
        var schedule = Assert.Single((await payer.Client.GetFromJsonAsync<List<PixScheduleResponse>>("/api/pix/schedules"))!);
        Assert.Equal((new DateOnly(2027, 1, 10), 1), (schedule.NextRunDate, schedule.Executions));

        // Next month there is only 50 left: the PIX fails, is not retried, and the customer is notified.
        factory.SetBrasiliaTime(new DateTime(2027, 1, 10, 9, 0, 0));
        await factory.RunScheduledPixJobAsync();
        schedule = Assert.Single((await payer.Client.GetFromJsonAsync<List<PixScheduleResponse>>("/api/pix/schedules"))!);
        Assert.Equal(("Saldo insuficiente.", new DateOnly(2027, 2, 10)), (schedule.LastError, schedule.NextRunDate));
        Assert.Equal(50m, (await payer.Client.AccountAsync()).AvailableBalance);

        var notifications = await TestUsers.EventuallyAsync(
            () => payer.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications")!,
            p => p!.Items.Any(n => n.Title == "PIX agendado não realizado"));
        Assert.Contains(notifications!.Items, n => n.Title == "PIX agendado não realizado");

        Assert.Equal(HttpStatusCode.NoContent, (await payer.Client.DeleteAsync($"/api/pix/schedules/{schedule.Id}")).StatusCode);
    }
}
