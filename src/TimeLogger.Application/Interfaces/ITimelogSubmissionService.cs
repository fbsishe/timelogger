using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Interfaces;

public enum SubmitOutcome { Succeeded, Failed, Skipped }

public interface ITimelogSubmissionService
{
    /// <summary>
    /// Submits a mapped <see cref="ImportedEntry"/> as a time registration to Timelog.
    /// Returns the outcome so callers can report accurate success/failure counts.
    /// Persists a <see cref="SubmittedEntry"/> audit record for non-skipped outcomes.
    /// </summary>
    Task<SubmitOutcome> SubmitAsync(ImportedEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits all mapped entries that have not yet been submitted (or previously failed).
    /// </summary>
    Task SubmitAllPendingAsync(CancellationToken cancellationToken = default);
}
