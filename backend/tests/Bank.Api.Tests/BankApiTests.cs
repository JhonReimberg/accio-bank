using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

public class BankApiTests(BankApiFactory factory) : IClassFixture<BankApiFactory>
{
    record TestUser(HttpClient Client, string Email, string Number);

    async Task<TestUser> NewUser(decimal deposit = 0)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.dev";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Teste", email, "senha-forte-123"));
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        if (deposit > 0)
            (await client.PostAsJsonAsync("/api/account/deposits", new DepositRequest(deposit))).EnsureSuccessStatusCode();
        return new TestUser(client, email, (await Account(client)).Number);
    }

    static async Task<AccountResponse> Account(HttpClient c) =>
        (await c.GetFromJsonAsync<AccountResponse>("/api/account"))!;

    static Task<HttpResponseMessage> Transfer(HttpClient c, string to, decimal amount) =>
        c.PostAsJsonAsync("/api/account/transfers", new TransferRequest(to, amount));

    [Fact]
    public async Task Register_creates_account_with_zero_balance_and_login_works()
    {
        var user = await NewUser();
        var account = await Account(user.Client);
        Assert.Equal(0m, account.Balance);
        Assert.True(long.Parse(account.Number) >= 10_000_001);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email.ToUpperInvariant(), "senha-forte-123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var wrong = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "errada-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        var user = await NewUser();
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("Outro", user.Email, "senha-forte-123"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Account_requires_token()
    {
        var res = await factory.CreateClient().GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public async Task Invalid_amounts_are_rejected(decimal amount)
    {
        var user = await NewUser();
        var res = await user.Client.PostAsJsonAsync("/api/account/deposits", new DepositRequest(amount));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Transfer_moves_money_and_writes_both_ledger_entries()
    {
        var alice = await NewUser(deposit: 100);
        var bob = await NewUser();

        Assert.Equal(HttpStatusCode.NoContent, (await Transfer(alice.Client, bob.Email, 30.50m)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Transfer(bob.Client, alice.Number, 0.50m)).StatusCode);

        Assert.Equal(70m, (await Account(alice.Client)).Balance);
        Assert.Equal(30m, (await Account(bob.Client)).Balance);
        var statement = await alice.Client.GetFromJsonAsync<List<TransactionResponse>>("/api/account/transactions");
        Assert.Equal([0.50m, -30.50m, 100m], statement!.Select(t => t.Amount));
    }

    [Fact]
    public async Task Transfer_rejects_insufficient_funds_unknown_recipient_and_self()
    {
        var alice = await NewUser(deposit: 10);
        var bob = await NewUser();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Transfer(alice.Client, bob.Email, 10.01m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Transfer(alice.Client, "ninguem@test.dev", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Transfer(alice.Client, alice.Number, 1)).StatusCode);
        Assert.Equal(10m, (await Account(alice.Client)).Balance);
        Assert.Equal(0m, (await Account(bob.Client)).Balance);
    }

    [Fact]
    public async Task Concurrent_transfers_never_overdraw()
    {
        var alice = await NewUser(deposit: 100);
        var bob = await NewUser();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Transfer(alice.Client, bob.Email, 10)));

        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0m, (await Account(alice.Client)).Balance);
        Assert.Equal(100m, (await Account(bob.Client)).Balance);
    }

    [Fact]
    public async Task Opposite_concurrent_transfers_do_not_deadlock_and_conserve_money()
    {
        var alice = await NewUser(deposit: 1000);
        var bob = await NewUser(deposit: 1000);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).SelectMany(_ => new[]
        {
            Transfer(alice.Client, bob.Email, 7),
            Transfer(bob.Client, alice.Email, 3),
        }));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(1000m - 20 * 7 + 20 * 3, (await Account(alice.Client)).Balance);
        Assert.Equal(2000m, (await Account(alice.Client)).Balance + (await Account(bob.Client)).Balance);
    }
}
