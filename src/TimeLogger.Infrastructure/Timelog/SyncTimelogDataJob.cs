using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// Hangfire recurring job — syncs Timelog projects and tasks on a daily schedule.
/// </summary>
public class SyncTimelogDataJob(
    ITimelogSyncService syncService,
    IJobHealthService jobHealth,
    ILogger<SyncTimelogDataJob> logger)
{
    public const string JobId = "timelog-sync";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SyncTimelogDataJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await syncService.SyncAsync(cancellationToken);
            logger.LogInformation("SyncTimelogDataJob completed successfully");
            await jobHealth.RecordSuccessAsync(JobId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SyncTimelogDataJob failed");
            await jobHealth.RecordFailureAsync(JobId, ex.Message, cancellationToken);
            throw; // Let Hangfire handle retry
        }
    }
}
