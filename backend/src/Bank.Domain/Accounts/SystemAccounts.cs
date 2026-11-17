namespace Bank.Domain.Accounts;

/// <summary>Well-known internal accounts, created by a migration.</summary>
public static class SystemAccounts
{
    /// <summary>Money entering or leaving the bank (simulated deposits). Its balance is the negative of all cash-in.</summary>
    public static readonly Guid Cash = new("00000000-0000-0000-0000-000000000001");
}
