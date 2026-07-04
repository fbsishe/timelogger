using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Tempo;

/// <summary>
/// Hangfire recurring job — pulls yesterday's Tempo worklogs for all active sources
/// and applies mapping rules. Submission is manual-only.
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
            await importService.ImportYesterdayAsync(cancellationToken);
            var mapped = await mappingService.ApplyAllPendingAsync(cancellationToken);
            logger.LogInformation("PullTempoWorklogsJob completed — {Mapped} entries mapped", mapped);
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
