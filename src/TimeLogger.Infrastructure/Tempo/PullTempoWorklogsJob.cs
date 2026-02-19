using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;

namespace TimeLogger.Infrastructure.Tempo;

/// <summary>
/// Hangfire recurring job — pulls yesterday's Tempo worklogs for all active sources.
/// </summary>
public class PullTempoWorklogsJob(
    ITempoImportService importService,
    ILogger<PullTempoWorklogsJob> logger)
{
    public const string JobId = "tempo-pull";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("PullTempoWorklogsJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await importService.ImportYesterdayAsync(cancellationToken);
            logger.LogInformation("PullTempoWorklogsJob completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PullTempoWorklogsJob failed");
            throw; // Let Hangfire handle retry
        }
    }
}
