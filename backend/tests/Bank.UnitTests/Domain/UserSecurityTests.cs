using Bank.Application.Auth;
using Bank.Domain.Common;
using Bank.Domain.Users;

namespace Bank.UnitTests.Domain;

public class UserSecurityTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static User NewUserWithPin(string pin = "2580")
    {
        var user = User.Create("Ana", "ana@test.dev", Now);
        user.SetTransactionPin(pin, p => $"hash:{p}");
        return user;
    }

    private static bool Verify(string hash, string pin) => hash == $"hash:{pin}";

    [Theory]
    [InlineData("1234")]
    [InlineData("4321")]
    [InlineData("0000")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    public void Weak_or_malformed_pins_are_rejected(string pin) =>
        Assert.Throws<DomainException>(() => TransactionPin.EnsureStrong(pin));

    [Theory]
    [InlineData("2580")]
    [InlineData("1357")]
    [InlineData("9021")]
    public void Reasonable_pins_are_accepted(string pin) => TransactionPin.EnsureStrong(pin);

    [Fact]
    public void Third_wrong_pin_locks_for_30_minutes_even_for_the_right_pin()
    {
        var user = NewUserWithPin();
        Assert.Equal("pin.invalid", Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now)).Code);
        Assert.Equal("pin.invalid", Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now)).Code);
        Assert.Equal("pin.locked_out", Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now)).Code);

        Assert.Equal("pin.locked_out", Assert.Throws<DomainException>(() => user.VerifyTransactionPin("2580", Verify, Now.AddMinutes(29))).Code);
        user.VerifyTransactionPin("2580", Verify, Now.AddMinutes(31));
    }

    [Fact]
    public void A_right_pin_resets_the_failure_counter()
    {
        var user = NewUserWithPin();
        Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now));
        Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now));
        user.VerifyTransactionPin("2580", Verify, Now);
        Assert.Equal("pin.invalid", Assert.Throws<DomainException>(() => user.VerifyTransactionPin("1111", Verify, Now)).Code);
    }

    [Fact]
    public void Fifth_failed_sign_in_locks_for_15_minutes_and_raises_an_alert()
    {
        var user = User.Create("Ana", "ana@test.dev", Now);
        for (var i = 0; i < User.MaxFailedLogins; i++) user.RegisterFailedSignIn(Now);

        Assert.Throws<DomainException>(() => user.EnsureCanSignIn(Now.AddMinutes(14)));
        user.EnsureCanSignIn(Now.AddMinutes(16));
        Assert.Single(user.DomainEvents.OfType<SignInLockedOut>());
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36", "Chrome no Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0 Safari/537.36 Edg/157.0", "Edge no Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 19_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.0 Mobile/15E148 Safari/604.1", "Safari no iOS")]
    [InlineData(null, "Dispositivo desconhecido")]
    public void Device_names_are_readable(string? userAgent, string expected) =>
        Assert.Equal(expected, DeviceNames.FromUserAgent(userAgent));
}
