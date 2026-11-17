using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Application.Auth;
using Bank.Application.Common;
using Bank.Application.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using OtpNet;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecurityTests(BankApiFactory factory)
{
    private HttpClient CookielessClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string RefreshCookieOf(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("accio_refresh=")).Split(';')[0];

    private static HttpRequestMessage RefreshWith(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_reusing_an_old_one_revokes_the_whole_session()
    {
        var user = await TestUsers.CreateAsync(factory);
        var client = CookielessClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, TestUsers.Password));
        var first = RefreshCookieOf(login);

        var rotated = await client.SendAsync(RefreshWith(first));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = RefreshCookieOf(rotated);
        Assert.NotEqual(first, second);

        // An attacker replays the stolen first token: rejected, and the legitimate newer token dies too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshWith(first))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshWith(second))).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var user = await TestUsers.CreateAsync(factory);
        var client = CookielessClient();
        var cookie = RefreshCookieOf(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, TestUsers.Password)));

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", cookie);
        await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshWith(cookie))).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_sign_in_even_with_the_right_password_and_alert_the_user()
    {
        var user = await TestUsers.CreateAsync(factory);
        var anonymous = factory.CreateClient();
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "senha-errada-1"))));

        var right = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, TestUsers.Password));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, right.StatusCode);
        Assert.Equal("auth.locked_out", (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var notifications = await TestUsers.EventuallyAsync(
            () => user.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications")!,
            p => p!.Items.Any(n => n.Title == "Acesso bloqueado temporariamente"));
        Assert.Contains(notifications!.Items, n => n.Title == "Acesso bloqueado temporariamente");
    }

    [Fact]
    public async Task Parallel_wrong_pins_cannot_bypass_the_three_attempt_limit()
    {
        var alice = await TestUsers.CreateAsync(factory, deposit: 100);
        var bob = await TestUsers.CreateAsync(factory);

        var guesses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => alice.Client.TransferAsync(bob.Email, 1, pin: $"{1000 + i * 37}")));
        Assert.All(guesses, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));

        // Even the right PIN is refused now: the lock was reached despite the parallel burst.
        var right = await alice.Client.TransferAsync(bob.Email, 1);
        Assert.Equal("pin.locked_out", (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(100m, (await alice.Client.AccountAsync()).AvailableBalance);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("9999")]
    [InlineData("12a4")]
    public async Task Weak_or_malformed_pins_are_rejected(string pin)
    {
        var user = await TestUsers.CreateAsync(factory);
        var res = await user.Client.PutAsJsonAsync("/api/security/pin", new SetPinRequest(TestUsers.Password, pin));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Two_factor_login_and_trusted_device_flow()
    {
        var user = await TestUsers.CreateAsync(factory);
        var setup = await (await user.Client.PostAsync("/api/security/2fa/setup", null)).Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
        Assert.StartsWith("otpauth://totp/", setup!.OtpAuthUri);
        var totp = new Totp(Base32Encoding.ToBytes(setup.Secret));
        Assert.Equal(HttpStatusCode.NoContent,
            (await user.Client.PostAsJsonAsync("/api/security/2fa/enable", new TwoFactorCodeRequest(totp.ComputeTotp(DateTime.UtcNow)))).StatusCode);

        var device = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", device);
        var step1 = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, TestUsers.Password)))
            .Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(step1!.RequiresTwoFactor);
        Assert.Null(step1.Session);

        // The challenge token is not an access token.
        var misuse = factory.CreateClient();
        misuse.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", step1.ChallengeToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await misuse.GetAsync("/api/account")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(step1.ChallengeToken!, "000000"))).StatusCode);
        var step2 = await (await client.PostAsJsonAsync("/api/auth/login/2fa",
                new TwoFactorLoginRequest(step1.ChallengeToken!, totp.ComputeTotp(DateTime.UtcNow), TrustDevice: true)))
            .Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(step2!.Session);

        // Trusted device: next login skips the code.
        var again = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, TestUsers.Password)))
            .Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(again!.RequiresTwoFactor);
    }

    [Fact]
    public async Task Sign_in_from_a_new_device_alerts_the_user_and_shows_in_the_access_history()
    {
        var user = await TestUsers.CreateAsync(factory, deviceId: "laptop");
        await TestUsers.LoginAsync(factory, user.Email, deviceId: "unknown-phone");

        var notifications = await TestUsers.EventuallyAsync(
            () => user.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications")!,
            p => p!.Items.Any(n => n.Title == "Acesso em novo dispositivo"));
        Assert.Contains(notifications!.Items, n => n.Title == "Acesso em novo dispositivo");

        var devices = await user.Client.GetFromJsonAsync<List<DeviceResponse>>("/api/security/devices");
        Assert.Equal(2, devices!.Count);
        var history = await user.Client.GetFromJsonAsync<PagedResult<LoginAttemptResponse>>("/api/security/access-history");
        Assert.Equal(2, history!.Items.Count(a => a.Succeeded));
    }
}
