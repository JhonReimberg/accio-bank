using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations add` run against this project alone; it never connects.</summary>
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BankDbContext>
{
    public BankDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<BankDbContext>();
        DependencyInjection.ConfigureDbContext(builder, "Host=localhost");
        return new BankDbContext(builder.Options, TimeProvider.System);
    }
}
