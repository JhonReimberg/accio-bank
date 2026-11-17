using System.Globalization;
using Bank.Application.Abstractions;
using Bank.Application.Accounts;
using Bank.Application.Admin;
using Bank.Application.Auditing;
using Bank.Application.Auth;
using Bank.Application.Credit;
using Bank.Application.Jobs;
using Bank.Application.Ledger;
using Bank.Application.Notifications;
using Bank.Application.Onboarding;
using Bank.Application.Pix;
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
        services.AddScoped<StatementService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AdminService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<KycReviewService>();
        services.AddScoped<ReconcileLedgerJob>();
        services.AddScoped<PixDirectory>();
        services.AddScoped<PixPayments>();
        services.AddScoped<PixService>();
        services.AddScoped<PixKeyService>();
        services.AddScoped<PixScheduleService>();
        services.AddScoped<PixScheduleRunner>();
        services.AddScoped<ExecuteScheduledPixJob>();
        services.AddScoped<IReceiptDetailsProvider, PixReceiptDetails>();

        services.AddScoped<Fraud.FraudService>();
        services.AddScoped<Cards.CardService>();
        services.AddScoped<Cards.CardInvoiceJob>();
        services.AddScoped<Investments.InvestmentService>();
        services.AddScoped<Investments.GuaranteedLoanService>();
        services.AddScoped<Investments.AccrueInvestmentYieldJob>();
        services.AddScoped<CreditData>();
        services.AddScoped<ScoreService>();
        services.AddScoped<CreditService>();
        services.AddScoped<BackofficeService>();
        services.AddScoped<RecalculateScoresJob>();
        services.AddScoped<LoanCollectionJob>();
        services.AddScoped<OverdraftInterestJob>();
        // The eligibility rules share one per-request profile (loaded once, read by all rules).
        services.AddScoped<EligibilityContext>();
        services.AddScoped<IEligibilityContext>(sp => sp.GetRequiredService<EligibilityContext>());
        services.AddScoped<IEligibilityRule, ActiveAccountRule>();
        services.AddScoped<IEligibilityRule, AccountAgeRule>();
        services.AddScoped<IEligibilityRule, MinimumMovementRule>();
        services.AddScoped<IEligibilityRule, NoOverdueRule>();
        services.AddScoped<IEligibilityRule, MinimumScoreRule>();
        services.AddScoped<IEligibilityRule, IncomeCommitmentRule>();
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
