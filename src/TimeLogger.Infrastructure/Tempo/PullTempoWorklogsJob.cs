using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Tempo;

/// <summary>
/// Hangfire recurring job — pulls everything created or amended in Tempo since the last
/// successful poll for all active sources, and applies mapping rules. Catching amendments
/// and back-dated worklogs is the point: a work-date window alone misses both.
/// </summary>
public class PullTempoWorklogsJob(
    ITempoImportService importService,
    IApplyMappingsService mappingService,
    IJobHealthService jobHealth,
    ILogger<PullTempoWorklogsJob> logger)
{
    public const string JobId = "tempo-pull";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("PullTempoWorklogsJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var pull = await importService.ImportIncrementalAsync(cancellationToken);
            var mapped = await mappingService.ApplyAllPendingAsync(cancellationToken);
            logger.LogInformation(
                "PullTempoWorklogsJob completed — {Imported} imported, {Refreshed} refreshed, "
                + "{Mapped} entries mapped, {Blocked} amended after submission",
                pull.Imported, pull.Refreshed, mapped, pull.ChangedAfterSubmission);
            await jobHealth.RecordSuccessAsync(JobId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PullTempoWorklogsJob failed");
            await jobHealth.RecordFailureAsync(JobId, ex.Message, cancellationToken);
            throw;
        }
    }
}
