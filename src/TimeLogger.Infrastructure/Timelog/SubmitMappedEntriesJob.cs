using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;

namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// Hangfire recurring job — submits all mapped entries to Timelog and records an audit trail.
/// </summary>
public class SubmitMappedEntriesJob(
    ITimelogSubmissionService submissionService,
    ILogger<SubmitMappedEntriesJob> logger)
{
    public const string JobId = "timelog-submit";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SubmitMappedEntriesJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await submissionService.SubmitAllPendingAsync(cancellationToken);
            logger.LogInformation("SubmitMappedEntriesJob completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SubmitMappedEntriesJob failed");
            throw;
        }
    }
}
