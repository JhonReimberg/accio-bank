using System.Globalization;
using Bank.Application.Abstractions;
using Bank.Application.Accounts;
using Bank.Application.Admin;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Jobs;
using Bank.Application.Ledger;
using Bank.Application.Notifications;
using Bank.Application.Onboarding;
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

        services.AddScoped<IAuditLog, AuditLogger>();
        services.AddScoped<LedgerPosting>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<SecurityService>();
        services.AddScoped<TransactionAuthorizer>();
        services.AddScoped<AccountService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AdminService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<KycReviewService>();
        services.AddScoped<ReconcileLedgerJob>();
        AddDomainEventHandlers(services);
        return services;
    }

    private static void AddDomainEventHandlers(IServiceCollection services)
    {
        var handlers =
            from type in typeof(DependencyInjection).Assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false }
            from contract in type.GetInterfaces()
            where contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)
            select (contract, type);
        foreach (var (contract, type) in handlers)
            services.AddScoped(contract, type);
    }
}
