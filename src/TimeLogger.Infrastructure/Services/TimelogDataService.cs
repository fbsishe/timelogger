using Hangfire;
using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Services;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Services;

public class TimelogDataService(AppDbContext db, IBackgroundJobClient jobs) : ITimelogDataService
{
    public async Task<IReadOnlyList<TimelogProjectSummary>> GetProjectsAsync(bool activeOnly = true, CancellationToken ct = default) =>
        await db.TimelogProjects
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new TimelogProjectSummary(
                p.Id, p.ExternalId, p.Name,
                p.Tasks.Count(t => t.IsActive),
                p.IsActive, p.LastSyncedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TimelogTaskSummary>> GetTasksByProjectAsync(
        int projectId, bool activeOnly = true, CancellationToken ct = default) =>
        await db.TimelogTasks
            .Where(t => t.TimelogProjectId == projectId && (!activeOnly || t.IsActive))
            .OrderBy(t => t.Name)
            .Select(t => new TimelogTaskSummary(t.Id, t.ExternalId, t.Name, t.IsActive))
            .ToListAsync(ct);

    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default) =>
        await db.TimelogProjects
            .OrderByDescending(p => p.LastSyncedAt)
            .Select(p => (DateTimeOffset?)p.LastSyncedAt)
            .FirstOrDefaultAsync(ct);

    public Task TriggerSyncAsync(CancellationToken ct = default)
    {
        jobs.Enqueue<SyncTimelogDataJob>(j => j.ExecuteAsync(CancellationToken.None));
        return Task.CompletedTask;
    }
}
