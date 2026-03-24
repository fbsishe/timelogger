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
    string? UserDisplay,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    int AttemptCount,
    string? ErrorMessage);

public record ReadyEntryItem(
    int Id,
    DateOnly WorkDate,
    string SourceName,
    string? IssueKey,
    string? Description,
    double Hours,
    string? UserDisplay,
    string? TimelogTaskName);

public record NeedsTaskItem(
    int Id,
    DateOnly WorkDate,
    string SourceName,
    string? IssueKey,
    string? Description,
    double Hours,
    string? UserDisplay,
    int TimelogProjectId,
    string ProjectName);

public record SubmissionBatchResult(int Succeeded, int Failed, int Skipped)
{
    public int Total => Succeeded + Failed + Skipped;
}

public interface ISubmissionService
{
    Task<int> GetReadyToSubmitCountAsync(CancellationToken ct = default);
    Task<int> GetSubmittedCountAsync(CancellationToken ct = default);
    Task<int> GetFailedCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubmissionHistoryItem>> GetRecentAsync(int limit = 200, string? accountIdFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReadyEntryItem>> GetReadyToSubmitAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<NeedsTaskItem>> GetNeedsTaskAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task AssignTaskAsync(int entryId, int taskId, CancellationToken ct = default);
    Task TriggerSubmitAllAsync(CancellationToken ct = default);
    Task<SubmissionBatchResult> SubmitSelectedAsync(IReadOnlyList<int> entryIds, CancellationToken ct = default);
    Task SkipAsync(int entryId, CancellationToken ct = default);
    Task AcknowledgeFailureAsync(int submittedEntryId, CancellationToken ct = default);
    Task AcknowledgeAllFailuresAsync(CancellationToken ct = default);
}
