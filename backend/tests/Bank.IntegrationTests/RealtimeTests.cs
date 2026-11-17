using Bank.Domain.Pix;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RealtimeTests(BankApiFactory factory)
{
    public record Pushed(Guid Id, string Title, string Body);

    [Fact]
    public async Task Pix_received_is_pushed_to_the_payee_over_signalr()
    {
        var payer = await TestUsers.CreateAsync(factory, deposit: 100);
        var payee = await TestUsers.CreateAsync(factory);
        await payee.Client.RegisterKeyAsync(PixKeyType.Email, payee.Email);

        var token = payee.Client.DefaultRequestHeaders.Authorization!.Parameter;
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/api/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // the test server has no real sockets
                o.AccessTokenProvider = () => Task.FromResult(token);
            })
            .Build();
        var received = new TaskCompletionSource<Pushed>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<Pushed>("notification", n => received.TrySetResult(n));
        await hub.StartAsync();

        await payer.Client.PixAsync(payee.Email, 25);

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("Dinheiro na conta", pushed.Title);
        Assert.Contains("PIX recebido", pushed.Body);
        Assert.Contains("25,00", pushed.Body);
    }
}
