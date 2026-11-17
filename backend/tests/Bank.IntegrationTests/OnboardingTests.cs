using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Application.Accounts;
using Bank.Application.Admin;
using Bank.Application.Onboarding;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OnboardingTests(BankApiFactory factory)
{
    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task New_accounts_wait_for_onboarding_and_cannot_move_money()
    {
        var (client, _) = await TestUsers.RegisterAsync(factory);

        var status = await client.GetFromJsonAsync<OnboardingStatusResponse>("/api/onboarding");
        Assert.Equal((OnboardingStep.PersonalData, KycStatus.NotSubmitted, AccountStatus.PendingReview),
            (status!.NextStep, status.KycStatus, status.AccountStatus));

        var deposit = await client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(10));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deposit.StatusCode);
        Assert.Equal("account.not_active", await CodeOf(deposit));
    }

    [Fact]
    public async Task Complete_onboarding_activates_the_account_with_the_income_tier()
    {
        var user = await TestUsers.CreateAsync(factory, income: 8_000);
        var account = await user.Client.AccountAsync();
        Assert.Equal((AccountStatus.Active, AccountTier.Plus), (account.Status, account.Tier));

        var status = await user.Client.GetFromJsonAsync<OnboardingStatusResponse>("/api/onboarding");
        Assert.Equal(OnboardingStep.Done, status!.NextStep);
        Assert.Matches(@"^\*\*\*\.\d{3}\.\d{3}-\*\*$", status.MaskedCpf);

        // Data is frozen after approval.
        var change = await user.Client.PutAsJsonAsync("/api/onboarding/financial-profile", new FinancialProfileRequest(99_000, "Outra"));
        Assert.Equal("onboarding.locked", await CodeOf(change));
    }

    [Fact]
    public async Task Personal_data_rules_cpf_age_and_uniqueness()
    {
        var (client, _) = await TestUsers.RegisterAsync(factory);
        var invalidCpf = await client.PutAsJsonAsync("/api/onboarding/personal-data",
            new PersonalDataRequest("Ana Souza", "123.456.789-00", new DateOnly(1990, 1, 1), "11912345678"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCpf.StatusCode);

        var cpf = Cpf.Generate(new Random());
        var underage = await client.PutAsJsonAsync("/api/onboarding/personal-data",
            new PersonalDataRequest("Ana Souza", cpf, DateOnly.FromDateTime(DateTime.Today.AddYears(-17)), "11912345678"));
        Assert.Equal("customer.underage", await CodeOf(underage));

        var ok = await client.PutAsJsonAsync("/api/onboarding/personal-data",
            new PersonalDataRequest("Ana Souza", Cpf.Format(cpf), new DateOnly(1990, 1, 1), "(11) 91234-5678"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var (other, _) = await TestUsers.RegisterAsync(factory);
        var duplicate = await other.PutAsJsonAsync("/api/onboarding/personal-data",
            new PersonalDataRequest("Outra Pessoa", cpf, new DateOnly(1990, 1, 1), "11912345678"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Cep_lookup_returns_the_address_or_404()
    {
        var (client, _) = await TestUsers.RegisterAsync(factory);
        var found = await client.GetFromJsonAsync<CepAddress>("/api/onboarding/cep/01310-100");
        Assert.Equal(("Avenida Paulista", "São Paulo", "SP"), (found!.Street, found.City, found.State));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/onboarding/cep/99999999")).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_checked_by_content_not_by_name()
    {
        var (client, _) = await TestUsers.RegisterAsync(factory);
        var fakeImage = await client.UploadAsync(DocumentType.IdentityDocument, "not an image"u8.ToArray(), "photo.png");
        Assert.Equal("upload.type", await CodeOf(fakeImage));

        var pdfSelfie = await client.UploadAsync(DocumentType.Selfie, "%PDF-1.7 fake"u8.ToArray(), "selfie.pdf");
        Assert.Equal("upload.type", await CodeOf(pdfSelfie));

        var tooBig = new byte[OnboardingService.MaxUploadBytes + 1];
        TestUsers.Png.CopyTo(tooBig, 0);
        Assert.Equal("upload.size", await CodeOf(await client.UploadAsync(DocumentType.IdentityDocument, tooBig)));

        Assert.Equal(HttpStatusCode.NoContent, (await client.UploadAsync(DocumentType.IdentityDocument, "%PDF-1.7 doc"u8.ToArray(), "rg.pdf")).StatusCode);
    }

    [Fact]
    public async Task Submitting_an_incomplete_onboarding_is_rejected()
    {
        var (client, _) = await TestUsers.RegisterAsync(factory);
        var res = await client.PostAsync("/api/onboarding/submit", null);
        Assert.Equal("onboarding.incomplete", await CodeOf(res));
    }

    [Fact]
    public async Task Blocked_accounts_cannot_send_but_still_receive()
    {
        var analyst = await TestUsers.CreateStaffAsync(factory, Bank.Domain.Users.Role.Analyst);
        var alice = await TestUsers.CreateAsync(factory, deposit: 50);
        var bob = await TestUsers.CreateAsync(factory, deposit: 50);
        var aliceAccountId = await AccountIds.OfAsync(factory, alice.Email);

        await analyst.PostAsJsonAsync($"/api/admin/accounts/{aliceAccountId}/block", new AccountStatusRequest("Suspeita de fraude"));

        Assert.Equal("account.blocked", await CodeOf(await alice.Client.TransferAsync(bob.Email, 10)));
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.TransferAsync(alice.Email, 10)).StatusCode);
        Assert.Equal(AccountStatus.Blocked, (await alice.Client.AccountAsync()).Status);
    }
}

public class ManualKycFactory : BankApiFactory
{
    protected override bool AutoApproveKyc => false;
}

/// <summary>Own factory: analysts review submissions by hand here.</summary>
public class KycReviewTests(ManualKycFactory factory) : IClassFixture<ManualKycFactory>
{
    [Fact]
    public async Task Analyst_rejects_then_approves_after_the_customer_fixes_the_data()
    {
        var analyst = await TestUsers.CreateStaffAsync(factory, Bank.Domain.Users.Role.Analyst);
        var (customer, email) = await TestUsers.RegisterAsync(factory);
        await TestUsers.CompleteOnboardingAsync(customer, income: 20_000);
        await customer.PostAsync("/api/onboarding/submit", null);

        var queue = await analyst.GetFromJsonAsync<Bank.Application.Common.PagedResult<KycQueueItem>>("/api/admin/kyc");
        var item = Assert.Single(queue!.Items, i => i.Email == email);
        Assert.StartsWith("***.", item.MaskedCpf);

        var detail = await analyst.GetFromJsonAsync<KycDetailResponse>($"/api/admin/kyc/{item.CustomerId}");
        var selfie = Assert.Single(detail!.Documents, d => d.Type == DocumentType.Selfie);
        var bytes = await analyst.GetByteArrayAsync($"/api/admin/kyc/{item.CustomerId}/documents/{selfie.Id}");
        Assert.Equal(TestUsers.Png, bytes);

        await analyst.PostAsJsonAsync($"/api/admin/kyc/{item.CustomerId}/reject", new RejectKycRequest("Selfie ilegível"));
        var rejected = await customer.GetFromJsonAsync<OnboardingStatusResponse>("/api/onboarding");
        Assert.Equal((KycStatus.Rejected, "Selfie ilegível", OnboardingStep.Submit), (rejected!.KycStatus, rejected.RejectionReason, rejected.NextStep));

        await customer.UploadAsync(DocumentType.Selfie, TestUsers.Png);
        await customer.PostAsync("/api/onboarding/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, (await analyst.PostAsync($"/api/admin/kyc/{item.CustomerId}/approve", null)).StatusCode);

        var account = await customer.AccountAsync();
        Assert.Equal((AccountStatus.Active, AccountTier.Premium), (account.Status, account.Tier));
    }

    [Fact]
    public async Task Customers_cannot_review_kyc()
    {
        var (customer, _) = await TestUsers.RegisterAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/kyc")).StatusCode);
    }
}
