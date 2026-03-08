using Hangfire;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Tempo;

/// <summary>
/// Hangfire recurring job — pulls yesterday's Tempo worklogs for all active sources,
/// applies mapping rules, then enqueues the submission job as a continuation.
/// </summary>
public class PullTempoWorklogsJob(
    ITempoImportService importService,
    IApplyMappingsService mappingService,
    IBackgroundJobClient jobs,
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

            if (mapped > 0)
                jobs.Enqueue<SubmitMappedEntriesJob>(j => j.ExecuteAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PullTempoWorklogsJob failed");
            throw;
        }
    }
}
