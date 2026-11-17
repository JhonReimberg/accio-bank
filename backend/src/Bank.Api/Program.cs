using Bank.Api.Auth;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration (Issuer, Key).");
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must have at least 32 characters.");

builder.Services.AddDbContext<BankDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Bank")));
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
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
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
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
    scope.ServiceProvider.GetRequiredService<BankDbContext>().Database.Migrate();

app.Run();

public partial class Program;
