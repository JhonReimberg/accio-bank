using System.Net;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Credit;
using Bank.Application.Investments;
using Bank.Application.Ledger;
using Bank.Domain.Investments;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

public class InvestmentTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    private static readonly DateTime Start = new(2026, 3, 2, 10, 0, 0);

    private static async Task<InvestmentResponse> Cdb(TestUser u) =>
        (await (await u.Client.PostAsJsonAsync("/api/investments", new OpenInvestmentRequest(InvestmentType.Cdb, "CDB", null)))
            .Content.ReadFromJsonAsync<InvestmentResponse>())!;

    private async Task AccrueDays(int days)
    {
        for (var i = 0; i < days; i++) await factory.RunJobAsync<AccrueInvestmentYieldJob>(j => j.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cdb_yields_cdi_and_redemption_withholds_regressive_iof_and_income_tax()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, deposit: 5_000);
        var cdb = await Cdb(user);
        var applied = await user.Client.PostIdempotentAsync($"/api/investments/{cdb.Id}/applications", new MoveInvestmentRequest(4_000, TestUsers.Pin));
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(1_000m, (await user.Client.AccountAsync()).AvailableBalance);

        await AccrueDays(10);
        var position = (await user.Client.GetFromJsonAsync<List<InvestmentResponse>>("/api/investments"))!.Single();
        Assert.InRange(position.Income, 15m, 17m); // 4,000 × ~0.0402%/day × 10 business days

        // Day 10: IOF still applies (66% of income), then IR 22.5% on what is left.
        factory.SetBrasiliaTime(Start.AddDays(10));
        var preview = (await user.Client.GetFromJsonAsync<RedemptionPreview>($"/api/investments/{cdb.Id}/redemptions/preview?amount={position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))!;
        Assert.Equal(Math.Round(position.Income * 0.66m, 2, MidpointRounding.ToEven), preview.Iof);
        Assert.True(preview.IncomeTax > 0);
        var redeemed = (await (await user.Client.PostIdempotentAsync($"/api/investments/{cdb.Id}/redemptions",
            new MoveInvestmentRequest(position.Value, TestUsers.Pin))).Content.ReadFromJsonAsync<RedemptionResponse>())!;
        Assert.Equal(preview.Net, redeemed.Net);
        Assert.Equal(1_000m + redeemed.Net, (await user.Client.AccountAsync()).AvailableBalance);

        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None)).IsBalanced);
    }

    [Fact]
    public async Task Guaranteed_loan_blocks_the_investment_and_uses_the_lower_rate()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, deposit: 3_000, income: 6_000);
        var cdb = await Cdb(user);
        await user.Client.PostIdempotentAsync($"/api/investments/{cdb.Id}/applications", new MoveInvestmentRequest(3_000, TestUsers.Pin));

        // New customer, no score history: the regular loan is not available, the guaranteed one is.
        var loan = await user.Client.PostIdempotentAsync("/api/credit/guaranteed-loans",
            new ContractGuaranteedLoanRequest(cdb.Id, 2_000, 12, TestUsers.Pin));
        Assert.Equal(HttpStatusCode.OK, loan.StatusCode);
        Assert.Equal(0.019m, (await loan.Content.ReadFromJsonAsync<LoanResponse>())!.MonthlyRate);

        var position = (await user.Client.GetFromJsonAsync<List<InvestmentResponse>>("/api/investments"))!.Single();
        Assert.Equal((2_000m, 1_000m), (position.BlockedAmount, position.AvailableToRedeem));
        var tooMuch = await user.Client.PostIdempotentAsync($"/api/investments/{cdb.Id}/redemptions", new MoveInvestmentRequest(1_500, TestUsers.Pin));
        Assert.Equal("investment.insufficient", await PixClient.CodeOf(tooMuch));
        var overCollateral = await user.Client.PostIdempotentAsync("/api/credit/guaranteed-loans",
            new ContractGuaranteedLoanRequest(cdb.Id, 1_000.01m, 12, TestUsers.Pin));
        Assert.Equal("investment.collateral", await PixClient.CodeOf(overCollateral));
    }

    [Fact]
    public async Task Caixinha_tracks_goal_progress_and_only_one_receives_round_ups()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, deposit: 1_000);
        var travel = (await (await user.Client.PostAsJsonAsync("/api/investments", new OpenInvestmentRequest(InvestmentType.Caixinha, "Viagem", 2_000)))
            .Content.ReadFromJsonAsync<InvestmentResponse>())!;
        var car = (await (await user.Client.PostAsJsonAsync("/api/investments", new OpenInvestmentRequest(InvestmentType.Caixinha, "Carro", null)))
            .Content.ReadFromJsonAsync<InvestmentResponse>())!;
        await user.Client.PostIdempotentAsync($"/api/investments/{travel.Id}/applications", new MoveInvestmentRequest(500, TestUsers.Pin));

        await user.Client.PutAsJsonAsync($"/api/investments/{travel.Id}/round-up", new RoundUpRequest(true));
        await user.Client.PutAsJsonAsync($"/api/investments/{car.Id}/round-up", new RoundUpRequest(true));

        var list = (await user.Client.GetFromJsonAsync<List<InvestmentResponse>>("/api/investments"))!;
        Assert.Equal(0.25m, list.Single(i => i.Id == travel.Id).GoalProgress);
        Assert.Equal([car.Id], list.Where(i => i.RoundUpCardPurchases).Select(i => i.Id));
    }
}
