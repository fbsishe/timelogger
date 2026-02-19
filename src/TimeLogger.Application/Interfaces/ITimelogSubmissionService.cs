using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Interfaces;

public interface ITimelogSubmissionService
{
    /// <summary>
    /// Submits a mapped <see cref="ImportedEntry"/> as a time registration to Timelog.
    /// Persists a <see cref="SubmittedEntry"/> audit record regardless of outcome.
    /// </summary>
    Task SubmitAsync(ImportedEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits all mapped entries that have not yet been submitted (or previously failed).
    /// </summary>
    Task SubmitAllPendingAsync(CancellationToken cancellationToken = default);
}
