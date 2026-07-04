using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;

namespace TimeLogger.Application.Services;

public record EmployeeSummary(string AccountId, string DisplayName);

public record ConflictEntryItem(
    int Id,
    DateOnly WorkDate,
    string SourceName,
    string? IssueKey,
    string? Description,
    double OurHours,
    double TimelogHours,
    string? UserDisplay,
    string? TimelogTaskName,
    string? TimelogRegistrationId);

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

public record SubmissionBatchResult(int Succeeded, int Failed, int Skipped, int Duplicates = 0, int Conflicts = 0)
{
    public int Total => Succeeded + Failed + Skipped + Duplicates + Conflicts;
}

/// <summary>Aggregated submitted hours for one employee within one Timelog project.</summary>
public record SubmissionSummaryRow(
    string Employee,
    string Project,
    int EntryCount,
    double Hours);

public interface ISubmissionService
{
    Task<int> GetReadyToSubmitCountAsync(CancellationToken ct = default);
    Task<int> GetSubmittedCountAsync(CancellationToken ct = default);
    Task<int> GetFailedCountAsync(CancellationToken ct = default);
    Task<int> GetConflictCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubmissionHistoryItem>> GetRecentAsync(int limit = 200, string? accountIdFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReadyEntryItem>> GetReadyToSubmitAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<NeedsTaskItem>> GetNeedsTaskAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ConflictEntryItem>> GetConflictsAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task AssignTaskAsync(int entryId, int taskId, CancellationToken ct = default);
    Task TriggerSubmitAllAsync(CancellationToken ct = default);
    Task<SubmissionBatchResult> SubmitSelectedAsync(IReadOnlyList<int> entryIds, CancellationToken ct = default);
    Task SkipAsync(int entryId, CancellationToken ct = default);
    Task AcknowledgeFailureAsync(int submittedEntryId, CancellationToken ct = default);
    Task AcknowledgeAllFailuresAsync(CancellationToken ct = default);
    Task<SubmitOutcome> ResolveConflictAsync(int entryId, ConflictResolution resolution, double? customHours = null, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeSummary>> GetEmployeeSummariesAsync(CancellationToken ct = default);

    /// <summary>
    /// Hours successfully submitted (incl. duplicates already in Timelog) per employee,
    /// grouped by Timelog project, for entries whose work date falls in the range.
    /// </summary>
    Task<IReadOnlyList<SubmissionSummaryRow>> GetSubmissionSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);
}
