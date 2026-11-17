using Bank.Api.Endpoints;
using Bank.Api.Infrastructure;
using Bank.Application;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Application.Jobs;
using Bank.Application.Pix;
using Bank.Infrastructure;
using Bank.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, log) =>
{
    log.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Bank.Api");
    // Structured JSON outside Development (log aggregators), readable text locally.
    if (builder.Environment.IsDevelopment()) log.WriteTo.Console();
    else log.WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuth(Bank.Infrastructure.DependencyInjection.GetJwtOptions(builder.Configuration));
builder.Services.AddApiRateLimiting(builder.Configuration.GetSection("RateLimiting").Get<RateLimitOptions>() ?? new RateLimitOptions());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApiDocs();
var behindProxy = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
if (behindProxy) builder.Services.AddTrustedProxy();

var app = builder.Build();

if (behindProxy) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diagnostics, http) =>
    diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value ?? "anonymous"));
app.UseIdempotencyBuffering();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapPixEndpoints();
app.MapCreditEndpoints();
app.MapInvestmentEndpoints();
app.MapOnboardingEndpoints();
app.MapSecurityEndpoints();
app.MapNotificationEndpoints();
app.MapAdminEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// shortcut: migrates on startup, fine for one instance; run migrations in the deploy pipeline when scaling out.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<BankDbContext>().Database.MigrateAsync();
    if (app.Configuration.GetValue("Seed:Enabled", false))
        await scope.ServiceProvider.GetRequiredService<Bank.Infrastructure.Persistence.Seeding.DemoDataSeeder>().SeedAsync(CancellationToken.None);
}

if (Bank.Infrastructure.DependencyInjection.JobsEnabled(app.Configuration))
{
    if (app.Environment.IsDevelopment())
        app.MapHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new DevelopmentDashboardAuthorization()] });
    var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
    var brasilia = new RecurringJobOptions { TimeZone = BankTime.SaoPaulo };
    jobs.AddOrUpdate<ReconcileLedgerJob>("reconcile-ledger", j => j.RunAsync(CancellationToken.None), "0 3 * * *", brasilia);
    // Every 10 minutes so a PIX scheduled for "today" goes out soon after it is created (good for the demo).
    jobs.AddOrUpdate<ExecuteScheduledPixJob>("scheduled-pix", j => j.RunAsync(CancellationToken.None), "*/10 * * * *", brasilia);
    jobs.AddOrUpdate<RecalculateScoresJob>("recalculate-scores", j => j.RunAsync(CancellationToken.None), "0 2 * * *", brasilia);
    jobs.AddOrUpdate<LoanCollectionJob>("loan-collection", j => j.RunAsync(CancellationToken.None), "0 8 * * *", brasilia);
    jobs.AddOrUpdate<Bank.Application.Investments.AccrueInvestmentYieldJob>("investment-yield", j => j.RunAsync(CancellationToken.None), "0 1 * * 2-6", brasilia);
    jobs.AddOrUpdate<OverdraftInterestJob>("overdraft-interest", j => j.RunAsync(CancellationToken.None), "55 23 * * *", brasilia);
}

app.Run();

public partial class Program;
