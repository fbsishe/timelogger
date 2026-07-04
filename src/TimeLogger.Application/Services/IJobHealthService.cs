namespace TimeLogger.Application.Services;

/// <summary>A job whose recent runs have failed repeatedly.</summary>
public record JobHealthStatus(
    string JobName,
    int ConsecutiveFailures,
    DateTimeOffset LastRunAt,
    string? LastError);

public interface IJobHealthService
{
    Task RecordSuccessAsync(string jobName, CancellationToken ct = default);

    /// <summary>Records a failed run and notifies the configured alert channel.</summary>
    Task RecordFailureAsync(string jobName, string errorMessage, CancellationToken ct = default);

    /// <summary>
    /// Returns jobs whose consecutive-failure count has reached the configured threshold,
    /// i.e. they have not succeeded recently and need attention.
    /// </summary>
    Task<IReadOnlyList<JobHealthStatus>> GetUnhealthyJobsAsync(CancellationToken ct = default);
}

/// <summary>Sends an alert about a failed background job to the configured channel(s).</summary>
public interface IJobFailureNotifier
{
    Task NotifyAsync(string jobName, string errorMessage, CancellationToken ct = default);
}
