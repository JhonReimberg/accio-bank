using Bank.Domain.Common;

namespace Bank.UnitTests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(2.345, 2.34)] // half to even: 4 is even, stays
    [InlineData(2.355, 2.36)] // half to even: 5 is odd, rounds up
    [InlineData(2.3451, 2.35)]
    [InlineData(-2.345, -2.34)]
    public void Round_uses_bankers_rounding(decimal value, decimal expected) =>
        Assert.Equal(expected, Money.Round(value));
}
