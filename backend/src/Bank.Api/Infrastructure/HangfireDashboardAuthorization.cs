using Hangfire.Dashboard;

namespace Bank.Api.Infrastructure;

/// <summary>The dashboard is only mapped in Development, where it is open to make the jobs visible in the demo.</summary>
internal class DevelopmentDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
