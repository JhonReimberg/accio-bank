using Bank.Application.Abstractions;
using Bank.Application.Accounts;
using Bank.Application.Auth;
using Bank.Infrastructure.Documents;
using Bank.Application.Onboarding;
using Bank.Application.Pix;
using Bank.Infrastructure.Email;
using Bank.Infrastructure.External;
using Bank.Infrastructure.Storage;
using Bank.Infrastructure.Idempotency;
using Bank.Infrastructure.Outbox;
using Bank.Infrastructure.Persistence;
using Bank.Infrastructure.Security;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Bank")
            ?? throw new InvalidOperationException("Missing connection string 'Bank'.");
        services.AddDbContext<BankDbContext>(o => ConfigureDbContext(o, connectionString));
        services.AddScoped<IBankDbContext>(sp => sp.GetRequiredService<BankDbContext>());
        services.AddScoped<IdempotencyStore>();
        services.AddScoped<Persistence.Seeding.DemoDataSeeder>();

        services.AddSingleton(GetJwtOptions(configuration));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISecretHasher, SecretHasher>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
        services.AddSingleton(configuration.GetSection("Sessions").Get<SessionOptions>() ?? new SessionOptions());
        services.AddSingleton(configuration.GetSection("Kyc").Get<KycOptions>() ?? new KycOptions());
        services.AddSingleton(configuration.GetSection("Pix").Get<PixOptions>() ?? new PixOptions());
        services.AddSingleton(configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions());
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        // Free Community license: fine for this non-commercial portfolio project.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddSingleton<IReceiptRenderer, QuestPdfReceiptRenderer>();
        services.AddHttpClient<ICepLookup, ViaCepLookup>(c =>
        {
            c.BaseAddress = new Uri(configuration["ViaCep:BaseUrl"] ?? "https://viacep.com.br/");
            c.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection()
            .SetApplicationName("vela-bank")
            .PersistKeysToDbContext<BankDbContext>();

        var outbox = configuration.GetSection("Outbox").Get<OutboxOptions>() ?? new OutboxOptions();
        services.AddSingleton(outbox);
        if (outbox.Enabled) services.AddHostedService<OutboxProcessor>();

        if (JobsEnabled(configuration))
        {
            services.AddHangfire(c => c
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
            services.AddHangfireServer();
        }

        services.AddHealthChecks().AddDbContextCheck<BankDbContext>("database", tags: ["ready"]);
        return services;
    }

    /// <summary>Hangfire (scheduled and recurring jobs). Off in integration tests, which call jobs directly.</summary>
    public static bool JobsEnabled(IConfiguration configuration) => configuration.GetValue("Jobs:Enabled", true);

    public static JwtOptions GetJwtOptions(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Missing 'Jwt' configuration (Issuer, Key).");
        if (jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must have at least 32 characters.");
        return jwt;
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
