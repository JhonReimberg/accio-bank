using System.Net;
using System.Net.Http.Json;
using Bank.Application.Boletos;
using Bank.Application.Ledger;
using Bank.Domain.Boletos;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

public class BoletoTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    private static readonly DateTime Start = new(2026, 3, 16, 10, 0, 0);
    private static DateOnly Today => DateOnly.FromDateTime(Start);

    /// <summary>A valid boleto of another (fictitious) bank, code 998.</summary>
    private static string External(decimal amount, DateOnly due)
    {
        var noDv = $"9989{BoletoCode.Factor(due):D4}{(long)(amount * 100):D10}{new string('7', 25)}";
        return BoletoCode.DigitableLine(noDv[..4] + BoletoCode.Mod11(noDv) + noDv[4..]);
    }

    private static Task<HttpResponseMessage> Pay(TestUser u, string code, DateOnly? on = null) =>
        u.Client.PostIdempotentAsync("/api/boletos/payments", new PayBoletoRequest(code, null, on, TestUsers.Pin));

    [Fact]
    public async Task Boleto_issued_by_one_customer_is_paid_once_by_another()
    {
        factory.SetBrasiliaTime(Start);
        var issuer = await TestUsers.CreateAsync(factory);
        var payer = await TestUsers.CreateAsync(factory, deposit: 500);

        var boleto = (await (await issuer.Client.PostAsJsonAsync("/api/boletos", new IssueBoletoRequest(150.75m, Today.AddDays(5), "Aula de violão")))
            .Content.ReadFromJsonAsync<BoletoResponse>())!;
        var info = await payer.Client.GetFromJsonAsync<BoletoInfo>($"/api/boletos/inspect?code={Uri.EscapeDataString(boleto.DigitableLine)}");
        Assert.Equal((150.75m, true, (DateOnly?)Today.AddDays(5)), (info!.Amount, info.IssuedByVela, info.DueDate));

        var paid = await (await Pay(payer, boleto.DigitableLine)).Content.ReadFromJsonAsync<BoletoPaymentResponse>();
        Assert.Equal(BoletoPaymentStatus.Paid, paid!.Status);
        Assert.Equal(349.25m, (await payer.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(150.75m, (await issuer.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(BoletoStatus.Paid, (await issuer.Client.GetFromJsonAsync<List<BoletoResponse>>("/api/boletos"))!.Single().Status);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pay(payer, boleto.DigitableLine)).StatusCode);
        var typo = boleto.DigitableLine.Replace(" ", "").Replace(".", "");
        typo = typo[..3] + (char)('0' + (typo[3] - '0' + 1) % 10) + typo[4..];
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pay(payer, typo)).StatusCode);
    }

    [Fact]
    public async Task Scheduled_payment_of_another_banks_boleto_runs_on_its_date_and_recharge_debits_the_account()
    {
        factory.SetBrasiliaTime(Start);
        var payer = await TestUsers.CreateAsync(factory, deposit: 300);
        var code = External(120, Today.AddDays(3));

        var scheduled = await (await Pay(payer, code, Today.AddDays(2))).Content.ReadFromJsonAsync<BoletoPaymentResponse>();
        Assert.Equal((BoletoPaymentStatus.Scheduled, BoletoPayments.ExternalBeneficiary), (scheduled!.Status, scheduled.BeneficiaryName));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pay(payer, code, Today.AddDays(4))).StatusCode); // after the due date
        Assert.Equal(300m, (await payer.Client.AccountAsync()).AvailableBalance);

        factory.SetBrasiliaTime(Start.AddDays(2));
        await factory.RunJobAsync<ExecuteScheduledBoletosJob>(j => j.RunAsync(CancellationToken.None));
        Assert.Equal(BoletoPaymentStatus.Paid, (await payer.Client.GetFromJsonAsync<List<BoletoPaymentResponse>>("/api/boletos/payments"))!.Single().Status);

        var recharge = await payer.Client.PostIdempotentAsync("/api/recharges", new RechargeRequest("Operadora Brisa", "(11) 91234-5678", 30, TestUsers.Pin));
        Assert.Equal("(11) 91234-5678", (await recharge.Content.ReadFromJsonAsync<RechargeResponse>())!.Phone);
        Assert.Equal(150m, (await payer.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await payer.Client.PostIdempotentAsync("/api/recharges", new RechargeRequest("Operadora Brisa", "11912345678", 33, TestUsers.Pin))).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None)).IsBalanced);
    }
}
