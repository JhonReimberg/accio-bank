using System.Net;
using System.Net.Http.Json;
using Bank.Application.Auth;

namespace Bank.IntegrationTests;

public class LowRateLimitFactory : BankApiFactory
{
    protected override int AuthPermitLimit => 3;
}

/// <summary>Own factory (and database) because it needs a tiny limit that would break every other test.</summary>
public class RateLimitTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("x@test.dev", "senha-errada-1"))).StatusCode);

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
    }
}
