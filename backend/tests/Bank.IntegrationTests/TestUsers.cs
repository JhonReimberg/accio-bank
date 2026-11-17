using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Auth;

namespace Bank.IntegrationTests;

public record TestUser(HttpClient Client, string Email, string Number);

internal static class TestUsers
{
    public const string Password = "senha-forte-123";

    public static async Task<TestUser> CreateAsync(BankApiFactory factory, decimal deposit = 0)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.dev";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Teste", email, Password));
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        if (deposit > 0)
            (await client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(deposit))).EnsureSuccessStatusCode();
        return new TestUser(client, email, (await client.AccountAsync()).Number);
    }

    public static Task<HttpResponseMessage> PostIdempotentAsync<T>(this HttpClient client, string url, T body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<AccountResponse> AccountAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<AccountResponse>("/api/account"))!;

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient client, string to, decimal amount, string? key = null) =>
        client.PostIdempotentAsync("/api/account/transfers", new TransferRequest(to, amount), key);
}
