using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Auth;
using Bank.Application.Onboarding;
using Bank.Domain.Customers;
using Bank.Domain.Users;
using Bank.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

public record TestUser(HttpClient Client, string Email, string Number);

internal static class TestUsers
{
    public const string Password = "senha-forte-123";
    public const string Pin = "2580";

    private static readonly Random Random = new();

    /// <summary>A user with an active account: registered, onboarded and (with auto-approve) approved.</summary>
    public static async Task<TestUser> CreateAsync(BankApiFactory factory, decimal deposit = 0, string? deviceId = null, decimal income = 3_000)
    {
        var (client, email) = await RegisterAsync(factory, deviceId);
        await CompleteOnboardingAsync(client, income);
        (await client.PostAsync("/api/onboarding/submit", null)).EnsureSuccessStatusCode();
        if (deposit > 0)
            (await client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(deposit))).EnsureSuccessStatusCode();
        return new TestUser(client, email, (await client.AccountAsync()).Number);
    }

    public static async Task<(HttpClient Client, string Email)> RegisterAsync(BankApiFactory factory, string? deviceId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId ?? Guid.NewGuid().ToString());
        var email = $"{Guid.NewGuid():N}@test.dev";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Teste", email, Password));
        res.EnsureSuccessStatusCode();
        var login = await res.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Session!.AccessToken);
        return (client, email);
    }

    /// <summary>Every onboarding step except the final submit.</summary>
    public static async Task CompleteOnboardingAsync(HttpClient client, decimal income = 3_000, string? cpf = null)
    {
        (await client.PutAsJsonAsync("/api/onboarding/personal-data",
            new PersonalDataRequest("Cliente Teste", cpf ?? Cpf.Generate(Random), new DateOnly(1990, 5, 20), "(11) 91234-5678"))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/onboarding/address",
            new AddressRequest("01310-100", "Avenida Paulista", "1000", "Apto 1", "Bela Vista", "São Paulo", "SP"))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/onboarding/financial-profile", new FinancialProfileRequest(income, "Desenvolvedora"))).EnsureSuccessStatusCode();
        (await client.UploadAsync(DocumentType.IdentityDocument, Png)).EnsureSuccessStatusCode();
        (await client.UploadAsync(DocumentType.Selfie, Png)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/security/pin", new SetPinRequest(Password, Pin))).EnsureSuccessStatusCode();
    }

    /// <summary>Smallest thing that passes the PNG signature check.</summary>
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, DocumentType type, byte[] bytes, string fileName = "file.png")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };
        return client.PostAsync($"/api/onboarding/documents/{type}", form);
    }

    /// <summary>Creates a back-office user: registers, promotes in the database, logs in again for a token with the role.</summary>
    public static async Task<HttpClient> CreateStaffAsync(BankApiFactory factory, Role role)
    {
        var user = await CreateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
            await db.Users.Where(u => u.Email == user.Email).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role));
        }
        return await LoginAsync(factory, user.Email);
    }

    public static async Task<HttpClient> LoginAsync(BankApiFactory factory, string email, string? deviceId = null, string password = Password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId ?? Guid.NewGuid().ToString());
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        res.EnsureSuccessStatusCode();
        var login = await res.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Session!.AccessToken);
        return client;
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var value = await probe();
            if (done(value) || DateTime.UtcNow > deadline) return value;
            await Task.Delay(100);
        }
    }

    public static Task<HttpResponseMessage> PostIdempotentAsync<T>(this HttpClient client, string url, T body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<AccountResponse> AccountAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<AccountResponse>("/api/account"))!;

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient client, string to, decimal amount, string? key = null, string pin = Pin) =>
        client.PostIdempotentAsync("/api/account/transfers", new TransferRequest(to, amount, pin), key);
}
