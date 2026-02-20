using TimeLogger.Domain;

namespace TimeLogger.Application.Services;

public record SubmissionHistoryItem(
    int Id,
    string? IssueKey,
    string? ProjectKey,
    string? Description,
    DateOnly WorkDate,
    double Hours,
    string SourceName,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    int AttemptCount,
    string? ErrorMessage);

public interface ISubmissionService
{
    Task<int> GetReadyToSubmitCountAsync(CancellationToken ct = default);
    Task<int> GetSubmittedCountAsync(CancellationToken ct = default);
    Task<int> GetFailedCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubmissionHistoryItem>> GetRecentAsync(int limit = 200, CancellationToken ct = default);
    Task TriggerSubmitAllAsync(CancellationToken ct = default);
}
