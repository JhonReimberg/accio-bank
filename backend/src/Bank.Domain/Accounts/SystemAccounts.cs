namespace Bank.Domain.Accounts;

/// <summary>Well-known internal accounts (the bank's own books), created by migrations.</summary>
public static class SystemAccounts
{
    /// <summary>Money entering or leaving the bank (simulated deposits). Its balance is the negative of all cash-in.</summary>
    public static readonly Guid Cash = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Loans receivable: debited on disbursement, credited back as principal is repaid.</summary>
    public static readonly Guid LoanPortfolio = new("00000000-0000-0000-0000-000000000002");

    /// <summary>Interest, fines and default interest earned on credit (loans and overdraft).</summary>
    public static readonly Guid CreditRevenue = new("00000000-0000-0000-0000-000000000003");

    /// <summary>IOF withheld from customers, owed to the government.</summary>
    public static readonly Guid IofPayable = new("00000000-0000-0000-0000-000000000004");
}
