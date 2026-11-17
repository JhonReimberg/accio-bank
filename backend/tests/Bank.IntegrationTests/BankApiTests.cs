using System.Net;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Domain.Ledger;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BankApiTests(BankApiFactory factory)
{
    [Fact]
    public async Task Register_creates_account_with_zero_balance_and_login_works()
    {
        var user = await TestUsers.CreateAsync(factory);
        var account = await user.Client.AccountAsync();
        Assert.Equal(0m, account.TotalBalance);
        Assert.True(long.Parse(account.Number) >= 10_000_001);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email.ToUpperInvariant(), TestUsers.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotNull((await login.Content.ReadFromJsonAsync<LoginResponse>())!.Session?.AccessToken);
        var wrong = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "errada-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        var user = await TestUsers.CreateAsync(factory);
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("Outro", user.Email, TestUsers.Password));
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
        var user = await TestUsers.CreateAsync(factory);
        var res = await user.Client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(amount));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Transfer_moves_money_and_writes_a_statement_line_on_each_side()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await alice.Client.TransferAsync(bob.Email, 30.50m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.TransferAsync(alice.Number, 0.50m)).StatusCode);

        Assert.Equal(70m, (await alice.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(30m, (await bob.Client.AccountAsync()).AvailableBalance);
        var statement = (await alice.Client.GetFromJsonAsync<PagedResult<StatementItem>>("/api/account/statement"))!.Items;
        Assert.Equal([0.50m, -30.50m, 100m], statement.Select(t => t.Amount));
        Assert.Equal([70m, 69.50m, 100m], statement.Select(t => t.BalanceAfter));
        Assert.Equal(TransactionKind.Deposit, statement[^1].Kind);
    }

    [Fact]
    public async Task Transfer_rejects_insufficient_funds_unknown_recipient_and_self()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 10);
        var bob = await TestUsers.CreateAsync(factory);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.Client.TransferAsync(bob.Email, 10.01m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.TransferAsync("ninguem@test.dev", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.TransferAsync("1", 1)).StatusCode); // system account is not a recipient
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.Client.TransferAsync(alice.Number, 1)).StatusCode);
        Assert.Equal(10m, (await alice.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(0m, (await bob.Client.AccountAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Concurrent_debits_never_overdraw()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => alice.Client.TransferAsync(bob.Email, 10)));

        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0m, (await alice.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(100m, (await bob.Client.AccountAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Opposite_concurrent_transfers_do_not_deadlock_and_conserve_money()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 1000);
        var bob = await TestUsers.CreateAsync(factory, deposit: 1000);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).SelectMany(_ => new[]
        {
            alice.Client.TransferAsync(bob.Email, 7),
            bob.Client.TransferAsync(alice.Email, 3),
        }));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1000m - 20 * 7 + 20 * 3, (await alice.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(2000m, (await alice.Client.AccountAsync()).TotalBalance + (await bob.Client.AccountAsync()).TotalBalance);
    }
}
