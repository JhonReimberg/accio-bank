using Bank.Application.Abstractions;
using Bank.Infrastructure.Persistence;
using Bank.Infrastructure.Security;
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

        services.AddSingleton(GetJwtOptions(configuration));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISecretHasher, SecretHasher>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

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
