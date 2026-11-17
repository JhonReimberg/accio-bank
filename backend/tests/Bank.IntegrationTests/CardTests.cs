using System.Net.Http.Json;
using Bank.Application.Cards;
using Bank.Application.Investments;
using Bank.Application.Ledger;
using Bank.Domain.Cards;
using Bank.Domain.Investments;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

public class CardTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    private static readonly DateTime Start = new(2026, 3, 15, 10, 0, 0);

    private static async Task<CardResponse> Issue(TestUser u, CardFunction function, CardForm form) =>
        (await (await u.Client.PostAsJsonAsync("/api/cards", new IssueCardRequest(function, form))).Content.ReadFromJsonAsync<CardResponse>())!;

    private static async Task<PurchaseResponse> Buy(TestUser u, Guid card, decimal amount, int installments = 1, bool online = true) =>
        (await (await u.Client.PostIdempotentAsync($"/api/cards/{card}/purchases", new PurchaseRequest("Loja Teste", amount, installments, online)))
            .Content.ReadFromJsonAsync<PurchaseResponse>())!;

    private static async Task<CreditLineResponse> Line(TestUser u) => (await u.Client.GetFromJsonAsync<CreditLineResponse>("/api/cards/credit-line"))!;

    [Fact]
    public async Task Debit_card_controls_and_round_up_to_the_caixinha()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, deposit: 500);
        var debit = await Issue(user, CardFunction.Debit, CardForm.Physical);
        Assert.Matches(@"^•••• \d{4}$", debit.Masked);

        var blockedOnline = await Buy(user, debit.Id, 99.90m, online: true);
        Assert.False(blockedOnline.Approved);
        Assert.Contains("online", blockedOnline.DeclineReason);

        var caixinha = (await (await user.Client.PostAsJsonAsync("/api/investments", new OpenInvestmentRequest(InvestmentType.Caixinha, "Troco", null)))
            .Content.ReadFromJsonAsync<InvestmentResponse>())!;
        await user.Client.PutAsJsonAsync($"/api/investments/{caixinha.Id}/round-up", new RoundUpRequest(true));

        var inStore = await Buy(user, debit.Id, 99.90m, online: false);
        Assert.True(inStore.Approved);
        Assert.Equal(0.10m, inStore.RoundUp);
        Assert.Equal(400m, (await user.Client.AccountAsync()).AvailableBalance); // 500 − 99.90 − 0.10
        Assert.Equal(0.10m, (await user.Client.GetFromJsonAsync<List<InvestmentResponse>>("/api/investments"))!.Single().Value);

        await user.Client.PatchAsJsonAsync($"/api/cards/{debit.Id}", new CardSettingsRequest(null, Blocked: true));
        Assert.False((await Buy(user, debit.Id, 1, online: false)).Approved);
        Assert.False((await Buy(user, debit.Id, 1_000, online: false)).Approved); // also no balance
    }

    [Fact]
    public async Task Credit_card_installments_temporary_card_minimum_payment_and_revolving_interest()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, deposit: 10_000, income: 20_000);
        var opened = await user.Client.PostAsJsonAsync("/api/cards/credit-line", new OpenCreditLineRequest(DueDay: 10));
        Assert.Equal(10_000m, (await opened.Content.ReadFromJsonAsync<CreditLineResponse>())!.Limit); // 50% of income

        var temporary = await Issue(user, CardFunction.Credit, CardForm.VirtualTemporary);
        Assert.True((await Buy(user, temporary.Id, 1_200, installments: 3)).Approved);
        Assert.False((await Buy(user, temporary.Id, 10)).Approved); // single use

        var line = await Line(user);
        Assert.Equal((8_800m, 3), (line.Available, line.Invoices.Count));
        Assert.All(line.Invoices, i => Assert.Equal(400m, i.Total));

        // Closing day (3rd, a week before the 10th): the April invoice closes. Pay only the minimum.
        var first = line.Invoices[0];
        factory.SetBrasiliaTime(first.ClosingDate.ToDateTime(new TimeOnly(9, 0)));
        await factory.RunJobAsync<CardInvoiceJob>(j => j.RunAsync(CancellationToken.None));
        Assert.Equal((InvoiceStatus.Closed, 60m), ((await Line(user)).Invoices[0].Status, (await Line(user)).Invoices[0].MinimumPayment));
        await user.Client.PostIdempotentAsync($"/api/cards/invoices/{first.Id}/payments", new PayInvoiceRequest(60, TestUsers.Pin));

        // After the due date the remaining 340 revolves with 12% interest into the next invoice.
        factory.SetBrasiliaTime(first.DueDate.AddDays(1).ToDateTime(new TimeOnly(9, 0)));
        await factory.RunJobAsync<CardInvoiceJob>(j => j.RunAsync(CancellationToken.None));
        line = await Line(user);
        Assert.Equal(InvoiceStatus.Revolved, line.Invoices[0].Status);
        var next = line.Invoices[1];
        Assert.Contains(next.Items, i => i.Type == InvoiceItemType.PreviousBalance && i.Amount == 340m);
        Assert.Contains(next.Items, i => i.Type == InvoiceItemType.RevolvingInterest && i.Amount == 40.80m);
        Assert.Equal(400m + 340m + 40.80m, next.Total);

        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None)).IsBalanced);
    }
}
