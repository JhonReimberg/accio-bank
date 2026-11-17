using Bank.Application.Onboarding;
using Bank.Domain.Common;
using Bank.Domain.Customers;

namespace Bank.UnitTests.Domain;

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void Valid_cpfs_pass_the_check_digits(string cpf) => Assert.True(Cpf.IsValid(cpf));

    [Theory]
    [InlineData("529.982.247-26")] // wrong second digit
    [InlineData("529.982.247-35")] // wrong first digit
    [InlineData("111.111.111-11")] // repeated digits pass the math but are invalid
    [InlineData("1234567890")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_cpfs_are_rejected(string? cpf) => Assert.False(Cpf.IsValid(cpf));

    [Fact]
    public void Generated_cpfs_are_always_valid()
    {
        var random = new Random(42);
        for (var i = 0; i < 1_000; i++) Assert.True(Cpf.IsValid(Cpf.Generate(random)));
    }

    [Fact]
    public void Format_and_mask() =>
        Assert.Equal(("529.982.247-25", "***.982.247-**"), (Cpf.Format("52998224725"), Cpf.Mask("52998224725")));
}

public class PhoneTests
{
    [Theory]
    [InlineData("(11) 91234-5678", "+5511912345678")]
    [InlineData("+55 21 98765-4321", "+5521987654321")]
    [InlineData("1133334444", "+551133334444")] // landline
    public void Brazilian_numbers_are_normalized_to_e164(string input, string expected) =>
        Assert.Equal(expected, Phone.TryNormalize(input));

    [Theory]
    [InlineData("11812345678")] // 11 digits must be a mobile (9 after the area code)
    [InlineData("0912345678")]  // area code below 11
    [InlineData("12345")]
    public void Invalid_numbers_are_rejected(string input) => Assert.Null(Phone.TryNormalize(input));
}

public class FileSignatureTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    public void Known_formats_are_detected_by_their_magic_bytes(byte[] header, string contentType) =>
        Assert.Equal(contentType, FileSignatures.Detect(header)?.ContentType);

    [Fact]
    public void Anything_else_is_unknown() => Assert.Null(FileSignatures.Detect("<html>"u8));
}

public class CustomerTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static Customer Complete()
    {
        var customer = Customer.Start(Guid.NewGuid(), Now);
        customer.SetPersonalData("Ana Souza", "52998224725", new DateOnly(1990, 1, 1), "+5511912345678", Today);
        customer.SetAddress(new Address("01310100", "Av. Paulista", "1000", null, "Bela Vista", "São Paulo", "SP"));
        customer.SetFinancialProfile(3_500, "Analista");
        customer.AttachDocument(DocumentType.IdentityDocument, "a.png", "image/png", 10, Now);
        customer.AttachDocument(DocumentType.Selfie, "b.png", "image/png", 10, Now);
        return customer;
    }

    [Fact]
    public void Next_step_walks_through_the_onboarding()
    {
        var customer = Customer.Start(Guid.NewGuid(), Now);
        Assert.Equal(OnboardingStep.PersonalData, customer.NextStep(hasTransactionPin: false));
        Assert.Equal(OnboardingStep.TransactionPin, Complete().NextStep(hasTransactionPin: false));
        Assert.Equal(OnboardingStep.Submit, Complete().NextStep(hasTransactionPin: true));
    }

    [Fact]
    public void Must_be_at_least_18()
    {
        var customer = Customer.Start(Guid.NewGuid(), Now);
        Assert.Throws<DomainException>(() =>
            customer.SetPersonalData("Ana Souza", "52998224725", Today.AddYears(-18).AddDays(1), "+5511912345678", Today));
        customer.SetPersonalData("Ana Souza", "52998224725", Today.AddYears(-18), "+5511912345678", Today);
    }

    [Fact]
    public void Submitted_data_is_locked_until_reviewed_and_editable_again_after_rejection()
    {
        var customer = Complete();
        customer.Submit(hasTransactionPin: true, Now);
        Assert.Throws<DomainException>(() => customer.SetFinancialProfile(10_000, "Outra"));

        customer.Reject(Guid.NewGuid(), "Documento ilegível", Now);
        customer.SetFinancialProfile(10_000, "Outra");
        customer.Submit(hasTransactionPin: true, Now);
        customer.Approve(Guid.NewGuid(), Now);
        Assert.Equal(KycStatus.Approved, customer.KycStatus);
    }

    [Fact]
    public void Uploading_again_replaces_the_document_of_the_same_type()
    {
        var customer = Complete();
        customer.AttachDocument(DocumentType.Selfie, "new.png", "image/png", 20, Now);
        Assert.Equal("new.png", Assert.Single(customer.Documents, d => d.Type == DocumentType.Selfie).StoragePath);
    }
}
