using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SeedTests(BankApiFactory factory)
{
    [Fact]
    public async Task Demo_seed_is_idempotent_ledger_consistent_and_usable()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
            await seeder.SeedAsync(CancellationToken.None); // second run is a no-op
        }

        using (var scope = factory.Services.CreateScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None);
            Assert.True(report.IsBalanced);
        }

        // The three credit situations the seed promises.
        var eduardo = await TestUsers.LoginAsync(factory, "eduardo@vela.dev", password: DemoDataSeeder.Password);
        var eduardoCredit = (await eduardo.GetFromJsonAsync<EligibilityResponse>("/api/credit/eligibility"))!;
        Assert.False(eduardoCredit.Rules.Single(r => r.Code == "no_overdue").Passed);
        var diego = await TestUsers.LoginAsync(factory, "diego@vela.dev", password: DemoDataSeeder.Password);
        Assert.False((await diego.GetFromJsonAsync<EligibilityResponse>("/api/credit/eligibility"))!.Rules.Single(r => r.Code == "account_age").Passed);

        var carla = await TestUsers.LoginAsync(factory, "carla@vela.dev", password: DemoDataSeeder.Password);
        var carlaCredit = (await carla.GetFromJsonAsync<EligibilityResponse>("/api/credit/eligibility"))!;
        Assert.True(carlaCredit.Eligible, string.Join("; ", carlaCredit.Rules.Where(r => !r.Passed).Select(r => r.Message)));
        var account = await carla.AccountAsync();
        Assert.Equal((AccountStatus.Active, AccountTier.Plus), (account.Status, account.Tier));

        // Running balances follow the chronological order of the seeded history.
        var statement = (await carla.GetFromJsonAsync<PagedResult<StatementItem>>("/api/account/statement?pageSize=100"))!.Items;
        Assert.Equal(account.TotalBalance, statement[0].BalanceAfter);
        for (var i = 0; i < statement.Count - 1; i++)
            Assert.Equal(statement[i + 1].BalanceAfter + statement[i].Amount, statement[i].BalanceAfter);
    }
}
