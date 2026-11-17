using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Application.Admin;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Notifications;
using Bank.Domain.Users;
using Bank.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlatformTests(BankApiFactory factory)
{
    [Fact]
    public async Task Customers_cannot_reach_the_back_office()
    {
        var customer = await TestUsers.CreateAsync(factory);
        var res = await customer.Client.GetAsync("/api/admin/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Analyst_holds_funds_which_become_unavailable_and_the_action_is_audited()
    {
        var analyst = await TestUsers.CreateStaffAsync(factory, Role.Analyst);
        var customer = await TestUsers.CreateAsync(factory, deposit: 100);
        var accountId = await AccountIdAsync(customer.Email);

        var hold = await analyst.PostAsJsonAsync($"/api/admin/accounts/{accountId}/holds", new PlaceHoldRequest(60, "Bloqueio judicial"));
        Assert.Equal(HttpStatusCode.OK, hold.StatusCode);
        var account = await customer.Client.AccountAsync();
        Assert.Equal((40m, 60m, 100m), (account.AvailableBalance, account.BlockedBalance, account.TotalBalance));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await customer.Client.TransferAsync(customer.Email, 50)).StatusCode);

        var holdId = (await hold.Content.ReadFromJsonAsync<HoldResponse>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await analyst.PostAsync($"/api/admin/holds/{holdId}/release", null)).StatusCode);
        Assert.Equal(100m, (await customer.Client.AccountAsync()).AvailableBalance);

        var logs = await analyst.GetFromJsonAsync<PagedResult<AuditLogResponse>>($"/api/admin/audit-logs?action=admin.hold&entityId={accountId}");
        Assert.Equal(["admin.hold.released", "admin.hold.placed"], logs!.Items.Select(l => l.Action));
        using var after = JsonDocument.Parse(logs.Items[1].After!);
        Assert.Equal(60m, after.RootElement.GetProperty("blockedBalance").GetDecimal());
    }

    [Fact]
    public async Task Only_admins_can_run_the_reconciliation_report()
    {
        var analyst = await TestUsers.CreateStaffAsync(factory, Role.Analyst);
        var admin = await TestUsers.CreateStaffAsync(factory, Role.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/admin/ledger/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/ledger/reconciliation")).StatusCode);
    }

    [Fact]
    public async Task Money_movements_and_logins_are_audited()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 50);
        var bob = await TestUsers.CreateAsync(factory);
        await alice.Client.TransferAsync(bob.Email, 20);
        await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(alice.Email, "senha-errada-1"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var aliceId = await db.Users.Where(u => u.Email == alice.Email).Select(u => u.Id).SingleAsync();
        var actions = await db.AuditLogs.Where(a => a.ActorUserId == aliceId).OrderBy(a => a.CreatedAt).Select(a => a.Action).ToListAsync();
        Assert.Equal(
            ["auth.registered", "auth.signed_in", "security.pin_created", "account.deposit", "account.transfer", "auth.login_failed"],
            actions);
    }

    [Fact]
    public async Task Received_money_produces_a_notification_through_the_outbox()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 50);
        var bob = await TestUsers.CreateAsync(factory);
        await alice.Client.TransferAsync(bob.Email, 12.5m);

        var page = await TestUsers.EventuallyAsync(
            () => bob.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications")!,
            p => p!.TotalCount > 0);

        var notification = Assert.Single(page!.Items);
        Assert.Equal("Dinheiro na conta", notification.Title);
        Assert.Contains("R$ 12,50", notification.Body);
    }

    [Fact]
    public async Task Health_endpoints_report_liveness_and_database_readiness()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private async Task<Guid> AccountIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        return await (from a in db.Accounts join u in db.Users on a.UserId equals u.Id where u.Email == email select a.Id).SingleAsync();
    }
}
