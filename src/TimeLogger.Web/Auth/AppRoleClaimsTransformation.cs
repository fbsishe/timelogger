using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Web.Auth;

public class AppRoleClaimsTransformation(IServiceScopeFactory scopeFactory) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var oid = principal.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(oid))
            return principal;

        // Already transformed in this request
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role))
            return principal;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.EntraObjectId == oid);

        if (user is null)
            return principal;

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        principal.AddIdentity(identity);

        return principal;
    }
}
