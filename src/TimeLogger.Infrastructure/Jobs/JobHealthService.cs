using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Services;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Jobs;

public class JobHealthOptions
{
    public const string SectionName = "JobHealth";

    /// <summary>Consecutive failures before a job is flagged unhealthy on the Dashboard.</summary>
    public int ConsecutiveFailureThreshold { get; set; } = 3;
}

public class JobHealthService(
    AppDbContext db,
    IJobFailureNotifier notifier,
    IOptions<JobHealthOptions> options,
    ILogger<JobHealthService> logger) : IJobHealthService
{
    public async Task RecordSuccessAsync(string jobName, CancellationToken ct = default)
    {
        db.JobExecutions.Add(new JobExecution
        {
            JobName = jobName,
            ExecutedAt = DateTimeOffset.UtcNow,
            Succeeded = true,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(string jobName, string errorMessage, CancellationToken ct = default)
    {
        db.JobExecutions.Add(new JobExecution
        {
            JobName = jobName,
            ExecutedAt = DateTimeOffset.UtcNow,
            Succeeded = false,
            ErrorMessage = errorMessage,
        });
        await db.SaveChangesAsync(ct);

        try
        {
            await notifier.NotifyAsync(jobName, errorMessage, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send failure notification for job {JobName}", jobName);
        }
    }

    public async Task<IReadOnlyList<JobHealthStatus>> GetUnhealthyJobsAsync(CancellationToken ct = default)
    {
        var threshold = Math.Max(1, options.Value.ConsecutiveFailureThreshold);

        // Recent runs are enough to determine the consecutive-failure streak
        var recent = await db.JobExecutions
            .OrderByDescending(e => e.ExecutedAt)
            .ThenByDescending(e => e.Id)
            .Take(500)
            .ToListAsync(ct);

        var unhealthy = new List<JobHealthStatus>();

        foreach (var group in recent.GroupBy(e => e.JobName))
        {
            var runs = group.OrderByDescending(e => e.ExecutedAt).ThenByDescending(e => e.Id).ToList();
            var streak = runs.TakeWhile(r => !r.Succeeded).ToList();

            if (streak.Count >= threshold)
            {
                unhealthy.Add(new JobHealthStatus(
                    group.Key,
                    streak.Count,
                    streak[0].ExecutedAt,
                    streak[0].ErrorMessage));
            }
        }

        return unhealthy.OrderByDescending(u => u.ConsecutiveFailures).ToList().AsReadOnly();
    }
}
