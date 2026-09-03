namespace TimeLogger.Application.Interfaces;

/// <summary>Outcome of an incremental Tempo pull.</summary>
/// <param name="Imported">Worklogs newly persisted as ImportedEntries.</param>
/// <param name="Refreshed">Already-imported entries updated because Tempo reported a change.</param>
/// <param name="ChangedAfterSubmission">
/// Entries Tempo reports as amended that we had already submitted to Timelog, so they were
/// left untouched to avoid desyncing. These need manual attention.
/// </param>
public record TempoImportResult(int Imported, int Refreshed, int ChangedAfterSubmission);

public interface ITempoImportService
{
    /// <summary>
    /// Fetches worklogs from the Tempo API for the given source and date range,
    /// enriches them with Jira issue metadata, and persists them as ImportedEntries.
    /// Existing entries are refreshed in place when Tempo reports them as amended.
    /// </summary>
    /// <returns>Number of new entries imported.</returns>
    Task<int> ImportAsync(int importSourceId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incremental pull for all enabled Tempo sources: everything created or amended in Tempo
    /// since that source's last successful poll, regardless of how far back the work date is.
    /// This is what catches back-dated worklogs — a date range alone cannot, because Tempo's
    /// from/to filter on the work date, not on when the worklog was entered.
    /// </summary>
    Task<TempoImportResult> ImportIncrementalAsync(CancellationToken cancellationToken = default);
}
