using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Bank.IntegrationTests;

/// <summary>Boots the real API against a disposable PostgreSQL container (one per test run).</summary>
public class BankApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Bank", _db.GetConnectionString());
        builder.UseSetting("Jwt:Issuer", "bank-tests");
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Outbox:PollIntervalMs", "100");
        builder.UseSetting("RateLimiting:AuthPermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("RateLimiting:SensitivePermitLimit", "100000");
    }

    /// <summary>High by default so tests never trip it; a dedicated factory lowers it to test the limiter.</summary>
    protected virtual int AuthPermitLimit => 100_000;

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<BankApiFactory>
{
    public const string Name = "api";
}
