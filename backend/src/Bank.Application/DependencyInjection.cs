using System.Globalization;
using Bank.Application.Accounts;
using Bank.Application.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Built-in pt-BR messages for every validator, regardless of the server culture.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("pt-BR");
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        return services;
    }
}
