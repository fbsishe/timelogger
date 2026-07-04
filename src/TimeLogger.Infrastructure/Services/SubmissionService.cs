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

    public async Task<int> GetConflictCountAsync(CancellationToken ct = default) =>
        await db.ImportedEntries
            .CountAsync(e => e.Status == ImportStatus.Conflict, ct);

    public async Task AcknowledgeFailureAsync(int submittedEntryId, CancellationToken ct = default)
    {
        var entry = await db.SubmittedEntries.FindAsync([submittedEntryId], ct)
            ?? throw new InvalidOperationException($"SubmittedEntry {submittedEntryId} not found.");
        entry.Status = SubmissionStatus.Acknowledged;
        await db.SaveChangesAsync(ct);
    }

    public async Task AcknowledgeAllFailuresAsync(CancellationToken ct = default)
    {
        var failed = await db.SubmittedEntries
            .Where(s => s.Status == SubmissionStatus.Failed)
            .ToListAsync(ct);
        foreach (var s in failed)
            s.Status = SubmissionStatus.Acknowledged;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SubmissionHistoryItem>> GetRecentAsync(
        int limit = 200, string? accountIdFilter = null, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.SubmittedEntry> query = db.SubmittedEntries
            .Where(s => s.Status != SubmissionStatus.Acknowledged);

        if (accountIdFilter != null)
            query = query.Where(s => s.ImportedEntry.UserEmail == accountIdFilter);

        var items = await query
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

    public async Task<IReadOnlyList<ReadyEntryItem>> GetReadyToSubmitAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        var query = db.ImportedEntries
            .Where(e => (e.Status == ImportStatus.Mapped || e.Status == ImportStatus.Failed)
                        && e.TimelogTaskId != null);

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        var entries = await query
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

    public async Task<IReadOnlyList<NeedsTaskItem>> GetNeedsTaskAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        var query = db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Mapped && e.TimelogTaskId == null);

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        var entries = await query
            .Include(e => e.ImportSource)
            .Include(e => e.TimelogProject)
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
            return new NeedsTaskItem(
                e.Id,
                e.WorkDate,
                e.ImportSource?.Name ?? "Unknown",
                e.IssueKey,
                e.Description,
                Math.Round(e.TimeSpentSeconds / 3600.0, 2),
                userDisplay,
                e.TimelogProjectId!.Value,
                e.TimelogProject?.Name ?? "Unknown");
        }).ToList();
    }

    public async Task AssignTaskAsync(int entryId, int taskId, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries.FindAsync([entryId], ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");
        entry.TimelogTaskId = taskId;
        await db.SaveChangesAsync(ct);
    }

    public Task TriggerSubmitAllAsync(CancellationToken ct = default)
    {
        jobs.Enqueue<SubmitMappedEntriesJob>(j => j.ExecuteAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    public async Task<SubmissionBatchResult> SubmitSelectedAsync(IReadOnlyList<int> entryIds, CancellationToken ct = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);

        int succeeded = 0, failed = 0, skipped = 0, duplicates = 0, conflicts = 0;
        foreach (var entry in entries)
        {
            switch (await submitter.SubmitAsync(entry, ct))
            {
                case SubmitOutcome.Succeeded:  succeeded++;  break;
                case SubmitOutcome.Failed:     failed++;     break;
                case SubmitOutcome.Skipped:    skipped++;    break;
                case SubmitOutcome.Duplicate:  duplicates++; break;
                case SubmitOutcome.Conflict:   conflicts++;  break;
            }
        }

        return new SubmissionBatchResult(succeeded, failed, skipped, duplicates, conflicts);
    }

    public async Task<IReadOnlyList<ConflictEntryItem>> GetConflictsAsync(string? accountIdFilter = null, CancellationToken ct = default)
    {
        var query = db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Conflict);

        if (accountIdFilter != null)
            query = query.Where(e => e.UserEmail == accountIdFilter);

        var entries = await query
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
            return new ConflictEntryItem(
                e.Id,
                e.WorkDate,
                e.ImportSource?.Name ?? "Unknown",
                e.IssueKey,
                e.Description,
                Math.Round(e.TimeSpentSeconds / 3600.0, 2),
                e.ConflictHoursInTimelog ?? 0,
                userDisplay,
                e.TimelogTask?.Name,
                e.ConflictTimelogRegistrationId);
        }).ToList();
    }

    public async Task<SubmitOutcome> ResolveConflictAsync(int entryId, ConflictResolution resolution, double? customHours = null, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries
            .Include(e => e.TimelogTask)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");
        return await submitter.ResolveConflictAsync(entry, resolution, customHours, ct);
    }

    public async Task SkipAsync(int entryId, CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries.FindAsync([entryId], ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");
        entry.Status = ImportStatus.Ignored;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeSummary>> GetEmployeeSummariesAsync(CancellationToken ct = default)
    {
        var mappings = await db.EmployeeMappings
            .Where(m => m.AtlassianAccountId != null)
            .OrderBy(m => m.DisplayName ?? m.TimelogUserDisplayName)
            .ToListAsync(ct);

        return mappings
            .Select(m => new EmployeeSummary(
                m.AtlassianAccountId!,
                m.DisplayName ?? m.TimelogUserDisplayName ?? m.AtlassianAccountId!))
            .ToList();
    }

    public async Task<IReadOnlyList<SubmissionSummaryRow>> GetSubmissionSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var submissions = await db.SubmittedEntries
            .Where(s => s.Status == SubmissionStatus.Success || s.Status == SubmissionStatus.Duplicate)
            .Include(s => s.ImportedEntry)
                .ThenInclude(e => e.TimelogProject)
            .Where(s => s.ImportedEntry.WorkDate >= from && s.ImportedEntry.WorkDate <= to)
            .ToListAsync(ct);

        var accountIds = submissions
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

        return submissions
            .GroupBy(s => new
            {
                Employee = s.ImportedEntry.UserEmail is { } email && mappings.TryGetValue(email, out var name)
                    ? name
                    : s.ImportedEntry.UserEmail ?? "(unknown)",
                Project = s.ImportedEntry.TimelogProject?.Name ?? "(no project)",
            })
            .Select(g => new SubmissionSummaryRow(
                g.Key.Employee,
                g.Key.Project,
                g.Count(),
                Math.Round(g.Sum(s => s.ImportedEntry.TimeSpentSeconds) / 3600.0, 2)))
            .OrderBy(r => r.Employee)
            .ThenBy(r => r.Project)
            .ToList()
            .AsReadOnly();
    }
}
