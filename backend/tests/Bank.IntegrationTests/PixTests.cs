using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Application.Accounts;
using Bank.Application.Common;
using Bank.Application.Pix;
using Bank.Domain.Pix;

namespace Bank.IntegrationTests;

internal static class PixClient
{
    public static async Task<PixKeyResponse> RegisterKeyAsync(this HttpClient client, PixKeyType type, string? value = null)
    {
        var res = await client.PostAsJsonAsync("/api/pix/keys", new RegisterPixKeyRequest(type, value));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PixKeyResponse>())!;
    }

    public static Task<HttpResponseMessage> PixAsync(this HttpClient client, string key, decimal? amount, string pin = TestUsers.Pin, string? brCode = null) =>
        client.PostIdempotentAsync("/api/pix/transfers", new PixTransferRequest(brCode is null ? key : null, brCode, amount, "teste", pin));

    public static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;
}

[Collection(ApiCollection.Name)]
public class PixTests(BankApiFactory factory)
{
    [Fact]
    public async Task Keys_follow_the_rules_and_point_to_a_single_account()
    {
        var ana = await TestUsers.CreateAsync(factory);
        var bia = await TestUsers.CreateAsync(factory);

        await ana.Client.RegisterKeyAsync(PixKeyType.Email, ana.Email);
        var phone = await ana.Client.RegisterKeyAsync(PixKeyType.Phone, "(21) 98888-7777");
        Assert.Equal("+5521988887777", phone.Value);
        var random = await ana.Client.RegisterKeyAsync(PixKeyType.Random);
        Assert.True(Guid.TryParse(random.Value, out _));

        var taken = await bia.Client.PostAsJsonAsync("/api/pix/keys", new RegisterPixKeyRequest(PixKeyType.Email, ana.Email));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        var notMyCpf = await bia.Client.PostAsJsonAsync("/api/pix/keys", new RegisterPixKeyRequest(PixKeyType.Cpf, "529.982.247-25"));
        Assert.Equal("pix.key_not_owner", await PixClient.CodeOf(notMyCpf));

        for (var i = 0; i < 2; i++) await ana.Client.RegisterKeyAsync(PixKeyType.Random);
        var sixth = await ana.Client.PostAsJsonAsync("/api/pix/keys", new RegisterPixKeyRequest(PixKeyType.Random, null));
        Assert.Equal("pix.key_limit", await PixClient.CodeOf(sixth));

        Assert.Equal(HttpStatusCode.NoContent, (await ana.Client.DeleteAsync($"/api/pix/keys/{random.Id}")).StatusCode);
        Assert.Equal(4, (await ana.Client.GetFromJsonAsync<List<PixKeyResponse>>("/api/pix/keys"))!.Count);
    }

    [Fact]
    public async Task Pix_by_key_moves_money_shows_a_masked_recipient_and_requires_the_pin()
    {
        var payer = await TestUsers.CreateAsync(factory, deposit: 200);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);

        var recipient = await payer.Client.GetFromJsonAsync<PixRecipientResponse>($"/api/pix/keys/lookup?key={payee.Email}");
        Assert.Equal("Cliente Teste", recipient!.Name);
        Assert.StartsWith("***.", recipient.MaskedCpf);

        Assert.Equal("pin.invalid", await PixClient.CodeOf(await payer.Client.PixAsync(payee.Email, 50, pin: "9137")));
        var sent = await payer.Client.PixAsync(payee.Email, 50);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var pix = await sent.Content.ReadFromJsonAsync<PixTransferResponse>();
        Assert.Matches("^E99999999", pix!.EndToEndId);

        Assert.Equal(150m, (await payer.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(50m, (await payee.Client.AccountAsync()).AvailableBalance);
        var statement = await payee.Client.GetFromJsonAsync<PagedResult<StatementItem>>("/api/account/statement?kind=Pix");
        Assert.Equal("PIX recebido de Cliente Teste", Assert.Single(statement!.Items).Description);

        Assert.Equal(HttpStatusCode.NotFound, (await payer.Client.PixAsync("ninguem@test.dev", 1)).StatusCode);
        Assert.Equal("pix.same_account", await PixClient.CodeOf(await payee.Client.PixAsync(payee.Email, 1)));
    }

    [Fact]
    public async Task Qr_code_with_amount_is_paid_with_copia_e_cola()
    {
        var payer = await TestUsers.CreateAsync(factory, deposit: 100);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Random);

        var qr = await (await payee.Client.PostAsJsonAsync("/api/pix/qrcodes", new CreateQrCodeRequest(null, 25.50m, "Pizza")))
            .Content.ReadFromJsonAsync<QrCodeResponse>();
        Assert.NotEmpty(Convert.FromBase64String(qr!.QrCodePngBase64));

        var decoded = await (await payer.Client.PostAsJsonAsync("/api/pix/brcode/decode", new DecodeBrCodeRequest(qr.Payload)))
            .Content.ReadFromJsonAsync<DecodedBrCodeResponse>();
        Assert.Equal((25.50m, "Pizza", "Cliente Teste"), (decoded!.Amount, decoded.Description, decoded.Recipient.Name));

        Assert.Equal("brcode.amount_mismatch", await PixClient.CodeOf(await payer.Client.PixAsync("", 30, brCode: qr.Payload)));
        Assert.Equal(HttpStatusCode.OK, (await payer.Client.PixAsync("", null, brCode: qr.Payload)).StatusCode);
        Assert.Equal(25.50m, (await payee.Client.AccountAsync()).AvailableBalance);

        var tampered = qr.Payload[..^1] + (qr.Payload[^1] == '0' ? '1' : '0');
        Assert.Equal("brcode.checksum", await PixClient.CodeOf(await payer.Client.PixAsync("", null, brCode: tampered)));
    }

    [Fact]
    public async Task Receiver_refunds_partially_and_concurrent_refunds_never_exceed_the_amount()
    {
        var payer = await TestUsers.CreateAsync(factory, deposit: 100);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);
        var pix = await (await payer.Client.PixAsync(payee.Email, 30)).Content.ReadFromJsonAsync<PixTransferResponse>();

        var detail = await payee.Client.GetFromJsonAsync<PixTransferDetail>($"/api/pix/transfers/by-transaction/{pix!.TransactionId}");
        Assert.True(detail!.CanRefund);
        Assert.False((await payer.Client.GetFromJsonAsync<PixTransferDetail>($"/api/pix/transfers/by-transaction/{pix.TransactionId}"))!.CanRefund);

        // The payer cannot "refund" their own payment.
        Assert.Equal(HttpStatusCode.NotFound,
            (await payer.Client.PostIdempotentAsync($"/api/pix/transfers/{pix.Id}/refunds", new PixRefundRequest(5, "x", TestUsers.Pin))).StatusCode);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            payee.Client.PostIdempotentAsync($"/api/pix/transfers/{pix.Id}/refunds", new PixRefundRequest(10, "Valor errado", TestUsers.Pin))));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(100m, (await payer.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(0m, (await payee.Client.AccountAsync()).AvailableBalance);

        var receipt = await payer.Client.GetAsync($"/api/account/statement/{pix.TransactionId}/receipt");
        Assert.Equal("application/pdf", receipt.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Favorite_contacts()
    {
        var ana = await TestUsers.CreateAsync(factory);
        var bia = await TestUsers.CreateAsync(factory);
        await bia.Client.RegisterKeyAsync(PixKeyType.Email, bia.Email);

        var contact = await (await ana.Client.PostAsJsonAsync("/api/pix/contacts", new AddContactRequest("Bia", bia.Email)))
            .Content.ReadFromJsonAsync<ContactResponse>();
        Assert.Equal(PixKeyType.Email, contact!.KeyType);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.Client.PostAsJsonAsync("/api/pix/contacts", new AddContactRequest("Bia 2", bia.Email))).StatusCode);
        Assert.Single((await ana.Client.GetFromJsonAsync<List<ContactResponse>>("/api/pix/contacts"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.Client.DeleteAsync($"/api/pix/contacts/{contact.Id}")).StatusCode);
    }
}
