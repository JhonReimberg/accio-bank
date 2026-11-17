using Bank.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.IntegrationTests;

internal static class AccountIds
{
    public static async Task<Guid> OfAsync(BankApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        return await (from a in db.Accounts join u in db.Users on a.UserId equals u.Id where u.Email == email select a.Id).SingleAsync();
    }
}
