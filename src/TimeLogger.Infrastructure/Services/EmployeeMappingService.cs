using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Services;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Jira;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Services;

public class EmployeeMappingService(
    AppDbContext db,
    ITimelogApiClient timelogApiClient,
    IJiraApiClient jiraApiClient,
    ILogger<EmployeeMappingService> logger) : IEmployeeMappingService
{
    public async Task<IReadOnlyList<EmployeeMappingDto>> GetAllAsync(CancellationToken ct = default)
    {
        var mappings = await db.EmployeeMappings
            .OrderBy(m => m.DisplayName ?? m.AtlassianAccountId)
            .ToListAsync(ct);

        var linkedUsers = await db.AppUsers
            .Where(u => u.EmployeeMappingId.HasValue)
            .Select(u => new { u.EmployeeMappingId, u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.EmployeeMappingId!.Value, u => new { u.Id, u.DisplayName }, ct);

        return mappings.Select(m =>
        {
            linkedUsers.TryGetValue(m.Id, out var lu);
            return new EmployeeMappingDto(
                m.Id,
                m.AtlassianAccountId,
                m.DisplayName,
                m.TimelogUserId,
                m.TimelogUserDisplayName,
                m.UpdatedAt,
                m.IsExcluded,
                lu?.Id,
                lu?.DisplayName);
        }).ToList();
    }

    public async Task UpsertAsync(UpsertEmployeeMappingRequest request, CancellationToken ct = default)
    {
        var existing = await db.EmployeeMappings
            .FirstOrDefaultAsync(m => m.AtlassianAccountId == request.AtlassianAccountId, ct);

        int mappingId;
        if (existing is null)
        {
            var mapping = new EmployeeMapping
            {
                AtlassianAccountId = request.AtlassianAccountId,
                DisplayName = request.DisplayName,
                TimelogUserId = request.TimelogUserId,
                TimelogUserDisplayName = request.TimelogUserDisplayName,
                IsExcluded = request.IsExcluded,
            };
            db.EmployeeMappings.Add(mapping);
            await db.SaveChangesAsync(ct);
            mappingId = mapping.Id;
        }
        else
        {
            existing.DisplayName = request.DisplayName;
            existing.TimelogUserId = request.TimelogUserId;
            existing.TimelogUserDisplayName = request.TimelogUserDisplayName;
            existing.IsExcluded = request.IsExcluded;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            mappingId = existing.Id;
        }

        // Clear any users currently linked to this mapping, then link the new one if provided
        var currentlyLinked = await db.AppUsers
            .Where(u => u.EmployeeMappingId == mappingId)
            .ToListAsync(ct);
        foreach (var u in currentlyLinked)
            u.EmployeeMappingId = null;

        if (request.AppUserId.HasValue)
        {
            var newUser = await db.AppUsers.FindAsync([request.AppUserId.Value], ct);
            if (newUser != null)
                newUser.EmployeeMappingId = mappingId;
        }

        if (currentlyLinked.Count > 0 || request.AppUserId.HasValue)
            await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var mapping = await db.EmployeeMappings.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"EmployeeMapping {id} not found.");
        db.EmployeeMappings.Remove(mapping);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<KnownAccountIdDto>> GetUnmappedAccountIdsAsync(CancellationToken ct = default)
    {
        var mappedIds = await db.EmployeeMappings
            .Select(m => m.AtlassianAccountId)
            .ToListAsync(ct);

        var accountIds = await db.ImportedEntries
            .Where(e => e.UserEmail != null && e.UserEmail.Contains(':'))
            .Select(e => e.UserEmail!)
            .Distinct()
            .Where(id => !mappedIds.Contains(id))
            .OrderBy(id => id)
            .ToListAsync(ct);

        var nameTasks = accountIds.Select(async id =>
        {
            var name = await FetchJiraDisplayNameAsync(id, ct);
            return new KnownAccountIdDto(id, name);
        });

        var results = await Task.WhenAll(nameTasks);
        return results.OrderBy(r => r.DisplayName ?? r.AccountId).ToList();
    }

    public async Task<IReadOnlyList<TimelogUserSummaryDto>> GetTimelogUsersAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await timelogApiClient.GetUsersAsync(ct);
            return response.Data
                .OrderBy(u => u.FullName)
                .Select(u => new TimelogUserSummaryDto(u.UserId, u.FullName, u.Email))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch Timelog users");
            return [];
        }
    }

    public async Task<IReadOnlyList<AppUserSummaryForMappingDto>> GetAppUsersAsync(CancellationToken ct = default) =>
        await db.AppUsers
            .OrderBy(u => u.DisplayName)
            .Select(u => new AppUserSummaryForMappingDto(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);

    public async Task<string?> FetchJiraDisplayNameAsync(string accountId, CancellationToken ct = default)
    {
        try
        {
            var user = await jiraApiClient.GetUserAsync(accountId, ct);
            return user.DisplayName;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Jira display name for {AccountId}", accountId);
            return null;
        }
    }
}
