namespace TimeLogger.Application.Interfaces;

public interface ITimelogSyncService
{
    /// <summary>
    /// Fetches all projects and their tasks from Timelog and upserts them into the local database.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the UTC timestamp of the last successful sync, or null if never synced.</summary>
    Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken cancellationToken = default);
}
