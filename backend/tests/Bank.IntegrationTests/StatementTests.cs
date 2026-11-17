using System.Net;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Common;
using Bank.Domain.Ledger;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StatementTests(BankApiFactory factory)
{
    private static Task<PagedResult<StatementItem>?> Statement(TestUser user, string query = "") =>
        user.Client.GetFromJsonAsync<PagedResult<StatementItem>>($"/api/account/statement{query}");

    [Fact]
    public async Task Statement_is_paginated_newest_first()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);
        for (var i = 1; i <= 4; i++) await alice.Client.TransferAsync(bob.Email, i);

        var page1 = await Statement(alice, "?pageSize=2");
        var page3 = await Statement(alice, "?pageSize=2&page=3");

        Assert.Equal((5, 3), (page1!.TotalCount, page1.TotalPages));
        Assert.Equal([-4m, -3m], page1.Items.Select(i => i.Amount));
        Assert.Equal([100m], page3!.Items.Select(i => i.Amount));
        Assert.All(page1.Items, i => Assert.Equal("Teste", i.Counterparty));
    }

    [Fact]
    public async Task Statement_filters_by_direction_kind_search_and_period()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory, deposit: 50);
        await alice.Client.TransferAsync(bob.Email, 10);
        await bob.Client.TransferAsync(alice.Email, 5);

        Assert.Equal([5m, 100m], (await Statement(alice, "?direction=In"))!.Items.Select(i => i.Amount));
        Assert.Equal([-10m], (await Statement(alice, "?direction=Out"))!.Items.Select(i => i.Amount));
        Assert.Equal([100m], (await Statement(alice, $"?kind={TransactionKind.Deposit}"))!.Items.Select(i => i.Amount));
        Assert.Equal([-10m], (await Statement(alice, "?search=transferência para"))!.Items.Select(i => i.Amount));

        var today = DateOnly.FromDateTime(BankTime.ToLocal(DateTime.UtcNow));
        Assert.Equal(3, (await Statement(alice, $"?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"))!.TotalCount);
        Assert.Equal(0, (await Statement(alice, $"?from={today.AddDays(1):yyyy-MM-dd}"))!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.Client.GetAsync($"/api/account/statement?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Receipt_is_a_pdf_available_only_to_the_parties()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);
        var stranger = await TestUsers.CreateAsync(factory);
        var transfer = await (await alice.Client.TransferAsync(bob.Email, 42.5m)).Content.ReadFromJsonAsync<OperationResponse>();

        foreach (var party in new[] { alice, bob })
        {
            var pdf = await party.Client.GetAsync($"/api/account/statement/{transfer!.TransactionId}/receipt");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.Client.GetAsync($"/api/account/statement/{transfer!.TransactionId}/receipt")).StatusCode);
    }
}
