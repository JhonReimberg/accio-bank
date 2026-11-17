using System.Net;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class IdempotencyAndLedgerTests(BankApiFactory factory)
{
    [Fact]
    public async Task Same_key_twice_runs_the_operation_once_and_replays_the_response()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);
        var key = Guid.NewGuid().ToString();

        var first = await alice.Client.TransferAsync(bob.Email, 25, key);
        var retry = await alice.Client.TransferAsync(bob.Email, 25, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<OperationResponse>())!.TransactionId,
            (await retry.Content.ReadFromJsonAsync<OperationResponse>())!.TransactionId);
        Assert.Equal(75m, (await alice.Client.AccountAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_produce_a_single_operation()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);
        var key = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => alice.Client.TransferAsync(bob.Email, 10, key)));

        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(90m, (await alice.Client.AccountAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_body_is_rejected()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);
        var key = Guid.NewGuid().ToString();

        await alice.Client.TransferAsync(bob.Email, 10, key);
        var other = await alice.Client.TransferAsync(bob.Email, 20, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal(90m, (await alice.Client.AccountAsync()).AvailableBalance);
    }

    [Fact]
    public async Task A_failed_operation_frees_the_key_for_a_retry()
    {
        var alice = await TestUsers.CreateAsync(factory);
        var bob = await TestUsers.CreateAsync(factory);
        var key = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.Client.TransferAsync(bob.Email, 10, key)).StatusCode);
        await alice.Client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(10));
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.TransferAsync(bob.Email, 10, key)).StatusCode);
    }

    [Fact]
    public async Task Money_operations_require_the_idempotency_header()
    {
        var alice = await TestUsers.CreateAsync(factory);
        var res = await alice.Client.PostAsJsonAsync("/api/account/deposits", new DepositRequest(10));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Ledger_stays_balanced_and_reconciles_with_materialized_balances()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 500);
        var bob = await TestUsers.CreateAsync(factory, deposit: 50);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => i % 2 == 0
            ? alice.Client.TransferAsync(bob.Email, 12.34m)
            : bob.Client.TransferAsync(alice.Email, 5.67m)));

        using var scope = factory.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None);

        Assert.True(report.IsBalanced, $"Mismatches: {string.Join(", ", report.Mismatches)}");
        Assert.Equal(report.TotalDebits, report.TotalCredits);
    }
}
