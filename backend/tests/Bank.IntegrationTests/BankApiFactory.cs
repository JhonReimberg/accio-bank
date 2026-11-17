using Bank.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Bank.IntegrationTests;

/// <summary>Boots the real API against a disposable PostgreSQL container (one per factory).</summary>
public class BankApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bank-tests", Guid.NewGuid().ToString("N"));

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
        builder.UseSetting("Kyc:AutoApprove", AutoApproveKyc.ToString());
        builder.UseSetting("Storage:Root", _storage);
        builder.ConfigureTestServices(services => services.AddSingleton<ICepLookup, FakeCepLookup>());
    }

    /// <summary>High by default so tests never trip it; a dedicated factory lowers it to test the limiter.</summary>
    protected virtual int AuthPermitLimit => 100_000;

    /// <summary>Most tests just need active accounts; the KYC review tests turn this off.</summary>
    protected virtual bool AutoApproveKyc => true;

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _db.DisposeAsync();
        if (Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<BankApiFactory>
{
    public const string Name = "api";
}

internal class FakeCepLookup : ICepLookup
{
    public Task<CepAddress?> LookupAsync(string cep, CancellationToken ct) =>
        Task.FromResult(cep == "01310100" ? new CepAddress(cep, "Avenida Paulista", "Bela Vista", "São Paulo", "SP") : null);
}
