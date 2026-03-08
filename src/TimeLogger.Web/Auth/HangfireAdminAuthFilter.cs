using Hangfire.Dashboard;

namespace TimeLogger.Web.Auth;

/// <summary>
/// Restricts the Hangfire dashboard to Admin users only.
/// </summary>
public class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.IsInRole("Admin");
    }
}
