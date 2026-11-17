using Bank.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

// Runs the real API against a real Postgres, in a throwaway database per test class.
public class BankApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString =
        $"{Environment.GetEnvironmentVariable("TEST_DB") ?? "Host=localhost;Port=5433;Username=bank;Password=bank"};Database=bank_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Bank", _connectionString);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BankDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
    }
}
