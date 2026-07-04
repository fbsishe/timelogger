using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// Hangfire recurring job — submits all mapped entries to Timelog and records an audit trail.
/// </summary>
public class SubmitMappedEntriesJob(
    ITimelogSubmissionService submissionService,
    IJobHealthService jobHealth,
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
            await jobHealth.RecordSuccessAsync(JobId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SubmitMappedEntriesJob failed");
            await jobHealth.RecordFailureAsync(JobId, ex.Message, cancellationToken);
            throw;
        }
    }
}
