using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;

namespace TimeLogger.Infrastructure.Tempo;

/// <summary>
/// Hangfire recurring job — pulls yesterday's Tempo worklogs for all active sources,
/// then immediately applies mapping rules to the new entries.
/// </summary>
public class PullTempoWorklogsJob(
    ITempoImportService importService,
    IApplyMappingsService mappingService,
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PullTempoWorklogsJob failed");
            throw;
        }
    }
}
