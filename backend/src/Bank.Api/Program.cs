using Bank.Api.Endpoints;
using Bank.Api.Infrastructure;
using Bank.Application;
using Bank.Application.Abstractions;
using Bank.Infrastructure;
using Bank.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwt = Bank.Infrastructure.DependencyInjection.GetJwtOptions(builder.Configuration);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new()
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Issuer,
            IssuerSigningKey = jwt.SigningKey,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapGet("/health", () => Results.Ok());

// shortcut: migrates on startup, fine for one instance; run migrations in the deploy pipeline when scaling out.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<BankDbContext>().Database.MigrateAsync();

app.Run();

public partial class Program;
