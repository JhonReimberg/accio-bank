using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Auth;
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

    public static async Task<TestUser> CreateAsync(BankApiFactory factory, decimal deposit = 0, string? deviceId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId ?? Guid.NewGuid().ToString());
        var email = $"{Guid.NewGuid():N}@test.dev";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Teste", email, Password));
        res.EnsureSuccessStatusCode();
        var login = await res.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Session!.AccessToken);
        (await client.PutAsJsonAsync("/api/security/pin", new SetPinRequest(Password, Pin))).EnsureSuccessStatusCode();
        if (deposit > 0)
            (await client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(deposit))).EnsureSuccessStatusCode();
        return new TestUser(client, email, (await client.AccountAsync()).Number);
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

    public static async Task<HttpClient> LoginAsync(BankApiFactory factory, string email, string? deviceId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId ?? Guid.NewGuid().ToString());
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
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
