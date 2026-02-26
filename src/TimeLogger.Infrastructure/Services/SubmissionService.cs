using Hangfire;
using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Services;

public class SubmissionService(
    AppDbContext db,
    IBackgroundJobClient jobs,
    ITimelogSubmissionService submitter) : ISubmissionService
{
    public async Task<int> GetReadyToSubmitCountAsync(CancellationToken ct = default) =>
        await db.ImportedEntries
            .CountAsync(e => e.Status == ImportStatus.Mapped && e.TimelogTaskId != null, ct);

    public async Task<int> GetSubmittedCountAsync(CancellationToken ct = default) =>
        await db.ImportedEntries
            .CountAsync(e => e.Status == ImportStatus.Submitted, ct);

    public async Task<int> GetFailedCountAsync(CancellationToken ct = default) =>
        await db.SubmittedEntries
            .CountAsync(s => s.Status == SubmissionStatus.Failed, ct);

    public async Task<IReadOnlyList<SubmissionHistoryItem>> GetRecentAsync(
        int limit = 200, CancellationToken ct = default)
    {
        var items = await db.SubmittedEntries
            .Include(s => s.ImportedEntry)
                .ThenInclude(e => e.ImportSource)
            .OrderByDescending(s => s.SubmittedAt)
            .Take(limit)
            .ToListAsync(ct);

        var accountIds = items
            .Select(s => s.ImportedEntry.UserEmail)
            .Where(e => e != null)
            .Distinct()
            .ToList();

        var mappings = accountIds.Count > 0
            ? await db.EmployeeMappings
                .Where(m => accountIds.Contains(m.AtlassianAccountId)
                            && (m.DisplayName != null || m.TimelogUserDisplayName != null))
                .ToDictionaryAsync(m => m.AtlassianAccountId,
                                   m => (m.DisplayName ?? m.TimelogUserDisplayName)!, ct)
            : [];

        return items.Select(s =>
        {
            var userEmail = s.ImportedEntry.UserEmail;
            var userDisplay = userEmail != null && mappings.TryGetValue(userEmail, out var name)
                ? name : userEmail;
            return new SubmissionHistoryItem(
                s.Id,
                s.ImportedEntry.IssueKey,
                s.ImportedEntry.ProjectKey,
                s.ImportedEntry.Description,
                s.ImportedEntry.WorkDate,
                Math.Round(s.ImportedEntry.TimeSpentSeconds / 3600.0, 2),
                s.ImportedEntry.ImportSource != null ? s.ImportedEntry.ImportSource.Name : "Unknown",
                userDisplay,
                s.Status,
                s.SubmittedAt,
                s.AttemptCount,
                s.ErrorMessage);
        }).ToList();
    }

    public async Task<IReadOnlyList<ReadyEntryItem>> GetReadyToSubmitAsync(CancellationToken ct = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => (e.Status == ImportStatus.Mapped || e.Status == ImportStatus.Failed)
                        && e.TimelogTaskId != null)
            .Include(e => e.ImportSource)
            .Include(e => e.TimelogTask)
            .OrderBy(e => e.WorkDate)
            .ToListAsync(ct);

        var accountIds = entries
            .Where(e => e.UserEmail != null)
            .Select(e => e.UserEmail!)
            .Distinct()
            .ToList();

        var mappings = accountIds.Count > 0
            ? await db.EmployeeMappings
                .Where(m => accountIds.Contains(m.AtlassianAccountId))
                .ToDictionaryAsync(m => m.AtlassianAccountId, m => m.DisplayName ?? m.TimelogUserDisplayName, ct)
            : [];

        return entries.Select(e =>
        {
            var userDisplay = e.UserEmail != null && mappings.TryGetValue(e.UserEmail, out var name)
                ? name : e.UserEmail;
            return new ReadyEntryItem(
                e.Id,
                e.WorkDate,
                e.ImportSource?.Name ?? "Unknown",
                e.IssueKey,
                e.Description,
                Math.Round(e.TimeSpentSeconds / 3600.0, 2),
                userDisplay,
                e.TimelogTask?.Name);
        }).ToList();
    }

    public Task TriggerSubmitAllAsync(CancellationToken ct = default)
    {
        jobs.Enqueue<SubmitMappedEntriesJob>(j => j.ExecuteAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    public async Task SubmitSelectedAsync(IReadOnlyList<int> entryIds, CancellationToken ct = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);

        foreach (var entry in entries)
            await submitter.SubmitAsync(entry, ct);
    }

    public async Task SkipAsync(int entryId, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries.FindAsync([entryId], ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");
        entry.Status = ImportStatus.Ignored;
        await db.SaveChangesAsync(ct);
    }
}
