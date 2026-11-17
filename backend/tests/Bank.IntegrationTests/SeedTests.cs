using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Common;
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

        var carla = await TestUsers.LoginAsync(factory, "carla@vela.dev", password: DemoDataSeeder.Password);
        var account = await carla.AccountAsync();
        Assert.Equal((AccountStatus.Active, AccountTier.Plus), (account.Status, account.Tier));

        // Running balances follow the chronological order of the seeded history.
        var statement = (await carla.GetFromJsonAsync<PagedResult<StatementItem>>("/api/account/statement?pageSize=100"))!.Items;
        Assert.Equal(account.TotalBalance, statement[0].BalanceAfter);
        for (var i = 0; i < statement.Count - 1; i++)
            Assert.Equal(statement[i + 1].BalanceAfter + statement[i].Amount, statement[i].BalanceAfter);
    }
}
