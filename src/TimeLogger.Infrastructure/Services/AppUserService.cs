using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Services;

public class AppUserService(
    AppDbContext db,
    ITimelogApiClient timelogApiClient,
    ILogger<AppUserService> logger) : IAppUserService
{
    public async Task<AppUser> EnsureUserAsync(string oid, string email, string displayName, CancellationToken ct = default)
    {
        var user = await db.AppUsers
            .Include(u => u.EmployeeMapping)
            .Include(u => u.AssignedProjects)
            .FirstOrDefaultAsync(u => u.EntraObjectId == oid, ct);

        if (user is null)
        {
            // Fall back to email lookup — handles seeded users whose OID isn't set yet
            var byEmail = await db.AppUsers
                .Include(u => u.EmployeeMapping)
                .Include(u => u.AssignedProjects)
                .FirstOrDefaultAsync(u => u.Email == email, ct);

            if (byEmail is not null)
            {
                byEmail.EntraObjectId = oid;
                byEmail.DisplayName = displayName;
                byEmail.LastLoginAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return byEmail;
            }

            user = new AppUser
            {
                EntraObjectId = oid,
                Email = email,
                DisplayName = displayName,
                Role = AppRole.User,
            };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
            await TryAutoMatchAsync(user, ct);
        }
        else
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
            if (user.Email != email) user.Email = email;
            if (user.DisplayName != displayName) user.DisplayName = displayName;
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    public async Task<AppUser?> GetByOidAsync(string oid, CancellationToken ct = default) =>
        await db.AppUsers
            .Include(u => u.EmployeeMapping)
            .Include(u => u.AssignedProjects)
            .FirstOrDefaultAsync(u => u.EntraObjectId == oid, ct);

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) =>
        await db.AppUsers
            .Include(u => u.EmployeeMapping)
            .Include(u => u.AssignedProjects).ThenInclude(p => p.TimelogProject)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

    public async Task SetRoleAsync(int userId, AppRole role, CancellationToken ct = default)
    {
        var user = await db.AppUsers.FindAsync([userId], ct)
            ?? throw new InvalidOperationException($"AppUser {userId} not found.");
        user.Role = role;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetProjectsAsync(int userId, IReadOnlyList<int> projectIds, CancellationToken ct = default)
    {
        var existing = await db.AppUserProjects
            .Where(up => up.AppUserId == userId)
            .ToListAsync(ct);
        db.AppUserProjects.RemoveRange(existing);

        foreach (var pid in projectIds)
            db.AppUserProjects.Add(new AppUserProject { AppUserId = userId, TimelogProjectId = pid });

        await db.SaveChangesAsync(ct);
    }

    private async Task TryAutoMatchAsync(AppUser user, CancellationToken ct)
    {
        try
        {
            var response = await timelogApiClient.GetUsersAsync(ct);
            var timelogUsers = response?.Data ?? [];

            // Match by email first, then by display name
            var match = timelogUsers.FirstOrDefault(u =>
                    !string.IsNullOrEmpty(u.Email) &&
                    string.Equals(u.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                ?? timelogUsers.FirstOrDefault(u =>
                    string.Equals(u.FullName, user.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (match is null) return;

            var mapping = await db.EmployeeMappings
                .FirstOrDefaultAsync(m => m.TimelogUserId == match.UserId, ct);

            if (mapping is null)
            {
                mapping = new EmployeeMapping
                {
                    AtlassianAccountId = user.Email,
                    DisplayName = user.DisplayName,
                    TimelogUserId = match.UserId,
                    TimelogUserDisplayName = match.FullName,
                };
                db.EmployeeMappings.Add(mapping);
                await db.SaveChangesAsync(ct);
            }

            user.EmployeeMappingId = mapping.Id;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Auto-matched user {Email} to Timelog user {TimelogUserId}", user.Email, match.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-match failed for user {Email} — skipping", user.Email);
        }
    }
}
