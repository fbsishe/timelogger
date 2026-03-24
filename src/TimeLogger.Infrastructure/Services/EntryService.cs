using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Services;

public class EntryService(AppDbContext db) : IEntryService
{
    public async Task<IReadOnlyList<EntryListItem>> GetUnmappedAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        var query = db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending || e.Status == ImportStatus.Failed);

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        var entries = await query
            .Include(e => e.ImportSource)
            .OrderByDescending(e => e.WorkDate)
            .ToListAsync(ct);

        var mappings = await GetMappingLookupAsync(entries.Select(e => e.UserEmail), ct);
        return entries.Select(e => ToListItem(e, mappings)).ToList();
    }

    public async Task<int> GetUnmappedCountAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        var query = db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending || e.Status == ImportStatus.Failed);

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        return await query.CountAsync(ct);
    }

    public async Task ManualMapAsync(int entryId, int timelogProjectId, int? timelogTaskId, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries.FindAsync([entryId], ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");

        entry.Status = ImportStatus.Mapped;
        entry.TimelogProjectId = timelogProjectId;
        entry.TimelogTaskId = timelogTaskId;
        await db.SaveChangesAsync(ct);
    }

    public async Task IgnoreAsync(int entryId, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries.FindAsync([entryId], ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");

        entry.Status = ImportStatus.Ignored;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EntryListItem>> GetAllAsync(int page, int pageSize, string? accountIdFilter = null, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.ImportedEntry> query = db.ImportedEntries;

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        var entries = await query
            .Include(e => e.ImportSource)
            .OrderByDescending(e => e.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var mappings = await GetMappingLookupAsync(entries.Select(e => e.UserEmail), ct);
        return entries.Select(e => ToListItem(e, mappings)).ToList();
    }

    public async Task<int> GetTotalCountAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        if (accountIdFilter != null)
            return await db.ImportedEntries.CountAsync(e => e.UserEmail == accountIdFilter, ct);

        return await db.ImportedEntries.CountAsync(ct);
    }

    private async Task<Dictionary<string, string>> GetMappingLookupAsync(
        IEnumerable<string?> userEmails, CancellationToken ct)
    {
        var accountIds = userEmails.Where(e => e != null).Select(e => e!).Distinct().ToList();
        if (accountIds.Count == 0) return [];

        return await db.EmployeeMappings
            .Where(m => accountIds.Contains(m.AtlassianAccountId)
                        && (m.DisplayName != null || m.TimelogUserDisplayName != null))
            .ToDictionaryAsync(m => m.AtlassianAccountId,
                               m => (m.DisplayName ?? m.TimelogUserDisplayName)!, ct);
    }

    private static EntryListItem ToListItem(
        Domain.Entities.ImportedEntry e,
        Dictionary<string, string>? mappings = null)
    {
        var displayUser = e.UserEmail != null && mappings != null && mappings.TryGetValue(e.UserEmail, out var name)
            ? name
            : e.UserEmail;
        return new(e.Id, e.ExternalId, e.ImportSource != null ? e.ImportSource.Name : "Unknown",
            e.WorkDate, Math.Round(e.TimeSpentSeconds / 3600.0, 2),
            e.ProjectKey, e.IssueKey, e.Description, displayUser, e.Status.ToString(), e.MetadataJson);
    }
}
