using Bank.Domain.Boletos;
using Bank.Domain.Common;

namespace Bank.UnitTests.Domain;

public class BoletoTests
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    [Fact]
    public void Issued_boleto_round_trips_through_the_digitable_line()
    {
        var boleto = Boleto.Issue(Guid.NewGuid(), "Ana", 1_234.56m, Today.AddDays(10), null, Today, DateTime.UtcNow, new Random(3));
        Assert.Equal(47, boleto.DigitableLine.Length);
        Assert.Equal(44, boleto.Barcode.Length);

        var parsed = BoletoCode.Parse(BoletoCode.Format(boleto.DigitableLine), Today);
        Assert.Equal((boleto.Barcode, "999", 1_234.56m, (DateOnly?)Today.AddDays(10)), (parsed.Barcode, parsed.BankCode, parsed.Amount, parsed.DueDate));
        Assert.Equal(boleto.Barcode, BoletoCode.Parse(boleto.Barcode, Today).Barcode);
    }

    [Fact]
    public void Mistyped_digits_are_rejected()
    {
        var line = Boleto.Issue(Guid.NewGuid(), "Ana", 99.90m, Today, null, Today, DateTime.UtcNow, new Random(5)).DigitableLine;
        // Fields 1-3 have mod-10 digits, which catch every single typo. The rest relies on the mod-11 digit, which by
        // design misses a few (remainders 0 and 1 both give digit 1), so it is checked on the amount only.
        foreach (var i in Enumerable.Range(0, 32).Append(45))
        {
            var typo = line[..i] + (char)('0' + (line[i] - '0' + 1) % 10) + line[(i + 1)..];
            Assert.Throws<DomainException>(() => BoletoCode.Parse(typo, Today));
        }
        Assert.Throws<DomainException>(() => BoletoCode.Parse("123", Today));
    }

    [Theory]
    [InlineData("2000-07-03", 1000)]
    [InlineData("2025-02-21", 9999)]
    [InlineData("2025-02-22", 1000)] // the factor restarted
    [InlineData("2026-03-15", 1386)]
    public void Due_date_factor_follows_febraban(string date, int factor) => Assert.Equal(factor, BoletoCode.Factor(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Check_digits_match_the_standard_examples()
    {
        Assert.Equal(5, BoletoCode.Mod10("001905009")); // field 1 printed as 00190.50095
        Assert.Equal(1, BoletoCode.Mod11("0000000000000000000000000000000000000000000")); // sum 0 → 11 → 1
    }
}
