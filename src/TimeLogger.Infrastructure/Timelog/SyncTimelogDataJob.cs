using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;

namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// Hangfire recurring job — syncs Timelog projects and tasks on a daily schedule.
/// </summary>
public class SyncTimelogDataJob(
    ITimelogSyncService syncService,
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SyncTimelogDataJob failed");
            throw; // Let Hangfire handle retry
        }
    }
}
