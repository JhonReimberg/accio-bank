using Bank.Domain.Fraud;

namespace Bank.UnitTests.Domain;

public class FraudPolicyTests
{
    [Theory]
    [InlineData(999, 100, 0, false, 0)]   // above 5x the average but below the minimum amount
    [InlineData(1_000, 100, 0, false, 1)] // above 5x and the minimum
    [InlineData(1_000, 0, 0, false, 0)]   // no history: the average rule does not apply
    [InlineData(2_000, 1_000, 0, true, 1)] // new device with a high amount
    [InlineData(10, 10, 4, false, 1)]     // fifth payment in 10 minutes
    [InlineData(5_000, 100, 4, true, 3)]
    public void Rules_flag_the_expected_signals(decimal amount, decimal average, int recent, bool newDevice, int expected) =>
        Assert.Equal(expected, new FraudPolicy().Evaluate(new FraudSignals(amount, average, recent, newDevice)).Count);

    [Fact]
    public void Disabled_policy_flags_nothing()
    {
        var policy = new FraudPolicy();
        policy.Update(false, 5, 1_000, 2_000, 5);
        Assert.Empty(policy.Evaluate(new FraudSignals(1_000_000, 1, 100, true)));
    }
}
