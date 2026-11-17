using System.Net;
using System.Net.Http.Json;
using Bank.Application.Accounts;
using Bank.Application.Admin;
using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Application.Ledger;
using Bank.Application.Notifications;
using Bank.Domain.Credit;
using Bank.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

/// <summary>Credit depends on time (account age, due dates, late days), so it runs on the fake clock.</summary>
public class CreditTests(FakeClockFactory factory) : IClassFixture<FakeClockFactory>
{
    private static readonly DateTime Start = new(2026, 1, 5, 10, 0, 0);

    private static Task<EligibilityResponse?> Eligibility(TestUser u) => u.Client.GetFromJsonAsync<EligibilityResponse>("/api/credit/eligibility");

    private static async Task<List<LoanResponse>> Loans(TestUser u) => (await u.Client.GetFromJsonAsync<List<LoanResponse>>("/api/credit/loans"))!;

    private Task RunCollection() => factory.RunJobAsync<LoanCollectionJob>(j => j.RunAsync(CancellationToken.None));

    /// <summary>A customer who becomes eligible: 61 days of account and 10k of movement in the last 30 days.</summary>
    private async Task<TestUser> EligibleCustomerAsync()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, income: 8_000);
        factory.SetBrasiliaTime(Start.AddDays(40));
        await user.Client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(10_000));
        factory.SetBrasiliaTime(Start.AddDays(61));
        return user;
    }

    [Fact]
    public async Task New_customer_sees_what_is_missing_instead_of_a_plain_no()
    {
        factory.SetBrasiliaTime(Start);
        var user = await TestUsers.CreateAsync(factory, income: 8_000);

        var result = (await Eligibility(user))!;
        Assert.False(result.Eligible);
        Assert.Null(result.Offer);
        var age = Assert.Single(result.Rules, r => r.Code == "account_age");
        Assert.False(age.Passed);
        Assert.Equal("Faltam 60 dias de conta (mínimo de 60)", age.Message);
        Assert.Equal(0m, age.Progress);
        Assert.True(Assert.Single(result.Rules, r => r.Code == "account_active").Passed);
        Assert.Equal(6, result.Rules.Count);

        var simulate = await user.Client.PostAsJsonAsync("/api/credit/loans/simulate", new SimulateLoanRequest(1_000, 12));
        Assert.Equal("credit.not_eligible", await PixClient.CodeOf(simulate));
    }

    [Fact]
    public async Task Eligible_customer_simulates_contracts_and_the_ledger_still_balances()
    {
        var user = await EligibleCustomerAsync();
        var result = (await Eligibility(user))!;
        Assert.True(result.Eligible, string.Join("; ", result.Rules.Where(r => !r.Passed).Select(r => r.Message)));
        Assert.Equal((8_000m, 0.069m), (result.Offer!.MaxAmount, result.Offer.MonthlyRate)); // score band 400: 1x income at 6.9% a.m.

        var quote = await (await user.Client.PostAsJsonAsync("/api/credit/loans/simulate", new SimulateLoanRequest(5_000, 12)))
            .Content.ReadFromJsonAsync<LoanQuote>();
        Assert.Equal(12, quote!.Schedule.Count);
        Assert.Equal(5_000m, quote.Schedule.Sum(r => r.Principal));

        var before = (await user.Client.AccountAsync()).AvailableBalance;
        var contract = await user.Client.PostIdempotentAsync("/api/credit/loans", new ContractLoanRequest(5_000, 12, TestUsers.Pin));
        Assert.Equal(HttpStatusCode.OK, contract.StatusCode);
        var loan = (await contract.Content.ReadFromJsonAsync<LoanResponse>())!;
        Assert.Equal(before + loan.NetAmount, (await user.Client.AccountAsync()).AvailableBalance);
        Assert.Equal(quote.InstallmentAmount, loan.InstallmentAmount);

        // Pre-approved amount shrinks by what is already owed.
        Assert.Equal(3_000m, (await Eligibility(user))!.Offer!.MaxAmount);
        var tooMuch = await user.Client.PostIdempotentAsync("/api/credit/loans", new ContractLoanRequest(3_000.01m, 12, TestUsers.Pin));
        Assert.Equal("credit.amount_above_offer", await PixClient.CodeOf(tooMuch));

        var pdf = await user.Client.GetAsync($"/api/credit/loans/{loan.Id}/contract");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync(CancellationToken.None)).IsBalanced);
    }

    [Fact]
    public async Task Installments_are_debited_on_due_date_and_late_ones_pay_fine_and_default_interest()
    {
        var user = await EligibleCustomerAsync();
        var loan = (await (await user.Client.PostIdempotentAsync("/api/credit/loans", new ContractLoanRequest(2_000, 6, TestUsers.Pin)))
            .Content.ReadFromJsonAsync<LoanResponse>())!;
        var first = loan.Installments[0];

        // Due date with money in the account: automatic debit.
        factory.SetBrasiliaTime(first.DueDate.ToDateTime(new TimeOnly(9, 0)));
        await RunCollection();
        Assert.Equal(InstallmentStatus.Paid, (await Loans(user))[0].Installments[0].Status);

        // Empty the account, then let installment 2 go 10 days late.
        var other = await TestUsers.CreateAsync(factory);
        var balance = (await user.Client.AccountAsync()).AvailableBalance;
        await user.Client.TransferAsync(other.Email, balance);
        var second = loan.Installments[1];
        factory.SetBrasiliaTime(second.DueDate.AddDays(10).ToDateTime(new TimeOnly(9, 0)));
        await RunCollection();

        var late = (await Loans(user))[0].Installments[1];
        Assert.Equal(InstallmentStatus.Overdue, late.Status);
        var expected = late.Amount + Math.Round(late.Amount * 0.02m, 2) + Math.Round(late.Amount * 0.00033m * 10, 2);
        Assert.Equal(expected, late.AmountDueToday);
        Assert.False((await Eligibility(user))!.Rules.Single(r => r.Code == "no_overdue").Passed);
        var notifications = await TestUsers.EventuallyAsync(
            () => user.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications")!,
            p => p!.Items.Any(n => n.Title == "Parcela em atraso"));
        Assert.Contains(notifications!.Items, n => n.Title == "Parcela em atraso");

        // Paying it by hand settles fine and default interest.
        await user.Client.PostIdempotentAsync("/api/account/deposits", new DepositRequest(1_000));
        var paid = await user.Client.PostIdempotentAsync($"/api/credit/loans/{loan.Id}/installments/2/pay", new PayInstallmentRequest(TestUsers.Pin));
        var after = (await paid.Content.ReadFromJsonAsync<LoanResponse>())!.Installments[1];
        Assert.Equal((InstallmentStatus.Paid, expected), (after.Status, after.PaidAmount));
    }

    [Fact]
    public async Task Paying_a_future_installment_early_costs_less_than_scheduled()
    {
        var user = await EligibleCustomerAsync();
        var loan = (await (await user.Client.PostIdempotentAsync("/api/credit/loans", new ContractLoanRequest(3_000, 12, TestUsers.Pin)))
            .Content.ReadFromJsonAsync<LoanResponse>())!;
        var last = loan.Installments[^1];
        Assert.True(last.AmountDueToday < last.Amount);
        Assert.Equal(last.Principal, last.AmountDueToday); // discount capped at that installment's interest

        var paid = (await (await user.Client.PostIdempotentAsync($"/api/credit/loans/{loan.Id}/installments/12/pay", new PayInstallmentRequest(TestUsers.Pin)))
            .Content.ReadFromJsonAsync<LoanResponse>())!;
        Assert.Equal(last.Principal, paid.Installments[^1].PaidAmount);
    }

    [Fact]
    public async Task Overdraft_is_offered_by_score_band_used_below_zero_and_charged_daily_interest()
    {
        var user = await EligibleCustomerAsync();
        var offer = await user.Client.GetFromJsonAsync<OverdraftResponse>("/api/credit/overdraft");
        Assert.Equal(500m, offer!.MaxLimitOffered);
        Assert.Equal("overdraft.above_offer",
            await PixClient.CodeOf(await user.Client.PutAsJsonAsync("/api/credit/overdraft", new SetOverdraftRequest(501, TestUsers.Pin))));
        await user.Client.PutAsJsonAsync("/api/credit/overdraft", new SetOverdraftRequest(500, TestUsers.Pin));

        var other = await TestUsers.CreateAsync(factory);
        var balance = (await user.Client.AccountAsync()).AvailableBalance;
        Assert.Equal(HttpStatusCode.OK, (await user.Client.TransferAsync(other.Email, balance + 400)).StatusCode);
        Assert.Equal(-400m, (await user.Client.AccountAsync()).TotalBalance);

        await factory.RunJobAsync<OverdraftInterestJob>(j => j.RunAsync(CancellationToken.None));
        var charged = (await user.Client.GetFromJsonAsync<PagedResult<StatementItem>>("/api/account/statement?kind=OverdraftInterest"))!.Items;
        var interest = Assert.Single(charged);
        Assert.InRange(-interest.Amount, 1m, 1.05m); // 400 × ((1.08)^(1/30) − 1) ≈ 1.03
        Assert.Equal("overdraft.below_usage",
            await PixClient.CodeOf(await user.Client.PutAsJsonAsync("/api/credit/overdraft", new SetOverdraftRequest(100, TestUsers.Pin))));
    }

    [Fact]
    public async Task Back_office_dashboard_customer_360_and_admin_only_policy()
    {
        var user = await EligibleCustomerAsync();
        await user.Client.PostIdempotentAsync("/api/credit/loans", new ContractLoanRequest(1_000, 6, TestUsers.Pin));
        var analyst = await TestUsers.CreateStaffAsync(factory, Role.Analyst);
        var admin = await TestUsers.CreateStaffAsync(factory, Role.Admin);

        var dashboard = await analyst.GetFromJsonAsync<DashboardResponse>("/api/admin/dashboard");
        Assert.True(dashboard!.CreditPortfolio >= 1_000m);
        Assert.True(dashboard.LedgerBalanced);

        var found = await analyst.GetFromJsonAsync<PagedResult<CustomerListItem>>($"/api/admin/customers?search={user.Email}");
        var view = await analyst.GetFromJsonAsync<Customer360>($"/api/admin/customers/{Assert.Single(found!.Items).UserId}");
        Assert.Single(view!.Loans);
        Assert.NotNull(view.Score);
        Assert.Matches(@"^\d{3}\.\d{3}\.\d{3}-\d{2}$", view.Cpf);

        var policy = (await admin.GetFromJsonAsync<CreditPolicy>("/api/admin/credit/policy"))!;
        var values = new CreditPolicyValues(30, policy.MinMovement30Days, policy.MinScore, policy.MaxIncomeCommitment, policy.MinInstallments,
            policy.MaxInstallments, policy.MinLoanAmount, policy.LateFeeRate, policy.DailyLateInterestRate, policy.OverdraftMonthlyRate,
            policy.CdiAnnualRate, policy.Bands);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PutAsJsonAsync("/api/admin/credit/policy", values)).StatusCode);
        var updated = await (await admin.PutAsJsonAsync("/api/admin/credit/policy", values)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(30, updated.GetProperty("minAccountAgeDays").GetInt32());
        Assert.Equal("credit_policy.invalid",
            await PixClient.CodeOf(await admin.PutAsJsonAsync("/api/admin/credit/policy", values with { LateFeeRate = 0.05m })));
    }
}
