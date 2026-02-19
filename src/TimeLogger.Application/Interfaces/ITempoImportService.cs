namespace TimeLogger.Application.Interfaces;

public interface ITempoImportService
{
    /// <summary>
    /// Fetches worklogs from the Tempo API for the given source and date range,
    /// enriches them with Jira issue metadata, and persists them as ImportedEntries.
    /// Skips entries already imported (deduplication by external ID).
    /// </summary>
    /// <returns>Number of new entries imported.</returns>
    Task<int> ImportAsync(int importSourceId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience method: imports yesterday's worklogs for all enabled Tempo sources.
    /// </summary>
    Task ImportYesterdayAsync(CancellationToken cancellationToken = default);
}
