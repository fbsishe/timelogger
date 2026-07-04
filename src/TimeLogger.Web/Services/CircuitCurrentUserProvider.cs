using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TimeLogger.Application.Services;

namespace TimeLogger.Web.Services;

/// <summary>
/// Resolves the signed-in user from the Blazor circuit's authentication state.
/// Returns null when there is no interactive circuit (e.g. Hangfire job scopes).
/// </summary>
public class CircuitCurrentUserProvider(AuthenticationStateProvider authStateProvider) : ICurrentUserProvider
{
    public async Task<string?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        try
        {
            var state = await authStateProvider.GetAuthenticationStateAsync();
            var user = state.User;
            if (user.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.Identity.Name;
        }
        catch (InvalidOperationException)
        {
            // No authentication state outside an interactive circuit
            return null;
        }
    }
}
