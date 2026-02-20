using Hangfire;
using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Services;

public class SubmissionService(AppDbContext db, IBackgroundJobClient jobs) : ISubmissionService
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
        int limit = 200, CancellationToken ct = default) =>
        await db.SubmittedEntries
            .Include(s => s.ImportedEntry)
                .ThenInclude(e => e.ImportSource)
            .OrderByDescending(s => s.SubmittedAt)
            .Take(limit)
            .Select(s => new SubmissionHistoryItem(
                s.Id,
                s.ImportedEntry.IssueKey,
                s.ImportedEntry.ProjectKey,
                s.ImportedEntry.Description,
                s.ImportedEntry.WorkDate,
                Math.Round(s.ImportedEntry.TimeSpentSeconds / 3600.0, 2),
                s.ImportedEntry.ImportSource != null ? s.ImportedEntry.ImportSource.Name : "Unknown",
                s.Status,
                s.SubmittedAt,
                s.AttemptCount,
                s.ErrorMessage))
            .ToListAsync(ct);

    public Task TriggerSubmitAllAsync(CancellationToken ct = default)
    {
        jobs.Enqueue<SubmitMappedEntriesJob>(j => j.ExecuteAsync(CancellationToken.None));
        return Task.CompletedTask;
    }
}
