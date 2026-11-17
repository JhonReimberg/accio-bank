using System.Net;
using System.Net.Http.Json;
using Bank.Application.Common;
using Bank.Application.Fraud;
using Bank.Application.Pix;
using Bank.Domain.Fraud;
using Bank.Domain.Pix;
using Bank.Domain.Users;

namespace Bank.IntegrationTests;

public class FraudTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    [Fact]
    public async Task Pix_far_above_the_average_is_held_then_approved_by_an_analyst()
    {
        factory.SetBrasiliaTime(new DateTime(2026, 3, 16, 14, 0, 0)); // daytime: PIX limits are higher
        var payer = await TestUsers.CreateAsync(factory, deposit: 5_000);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);
        await payer.Client.PixAsync(payee.Email, 100); // average = 100

        var big = await (await payer.Client.PixAsync(payee.Email, 1_500)).Content.ReadFromJsonAsync<PixTransferResponse>();
        Assert.Equal(PixTransferStatus.UnderReview, big!.Status);
        var account = await payer.Client.AccountAsync();
        Assert.Equal((1_500m, 3_400m), (account.BlockedBalance, account.AvailableBalance)); // held, not sent
        Assert.Equal(100m, (await payee.Client.AccountAsync()).AvailableBalance);

        var mine = await payer.Client.GetFromJsonAsync<List<FraudReviewResponse>>("/api/fraud/reviews");
        Assert.Contains("acima de 5x a média", Assert.Single(mine!).Reasons);

        var analyst = await TestUsers.CreateStaffAsync(factory, Role.Analyst);
        var queue = await analyst.GetFromJsonAsync<PagedResult<FraudReviewResponse>>("/api/admin/fraud/reviews");
        Assert.Contains(queue!.Items, r => r.Id == big.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await analyst.PostAsync($"/api/admin/fraud/reviews/{big.Id}/approve", null)).StatusCode);

        account = await payer.Client.AccountAsync();
        Assert.Equal((0m, 3_400m), (account.BlockedBalance, account.AvailableBalance));
        Assert.Equal(1_600m, (await payee.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await analyst.PostAsync($"/api/admin/fraud/reviews/{big.Id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Rejected_review_releases_the_money_and_velocity_rule_catches_bursts()
    {
        factory.SetBrasiliaTime(new DateTime(2026, 3, 16, 14, 0, 0)); // daytime: PIX limits are higher
        var payer = await TestUsers.CreateAsync(factory, deposit: 1_000);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);
        for (var i = 0; i < 4; i++) await payer.Client.PixAsync(payee.Email, 10);

        var fifth = await (await payer.Client.PixAsync(payee.Email, 10)).Content.ReadFromJsonAsync<PixTransferResponse>();
        Assert.Equal(PixTransferStatus.UnderReview, fifth!.Status);

        var analyst = await TestUsers.CreateStaffAsync(factory, Role.Analyst);
        await analyst.PostAsync($"/api/admin/fraud/reviews/{fifth.Id}/reject", null);
        var account = await payer.Client.AccountAsync();
        Assert.Equal((0m, 960m), (account.BlockedBalance, account.AvailableBalance));
        Assert.Equal(FraudReviewStatus.Rejected, (await payer.Client.GetFromJsonAsync<List<FraudReviewResponse>>("/api/fraud/reviews"))!.Single().Status);
    }
}
