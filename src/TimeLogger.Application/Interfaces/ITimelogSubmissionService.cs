using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Interfaces;

public enum SubmitOutcome { Succeeded, Failed, Skipped, Duplicate, Conflict }

public interface ITimelogSubmissionService
{
    /// <summary>
    /// Submits a mapped <see cref="ImportedEntry"/> as a time registration to Timelog.
    /// Checks for an existing registration first — returns Duplicate if hours match, Conflict if they differ.
    /// Persists a <see cref="SubmittedEntry"/> audit record for non-skipped/non-conflict outcomes.
    /// </summary>
    Task<SubmitOutcome> SubmitAsync(ImportedEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits all mapped entries that have not yet been submitted (or previously failed).
    /// </summary>
    Task SubmitAllPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a conflict on an entry that previously returned <see cref="SubmitOutcome.Conflict"/>.
    /// Updates the existing Timelog registration according to the chosen resolution mode.
    /// </summary>
    Task<SubmitOutcome> ResolveConflictAsync(
        ImportedEntry entry,
        ConflictResolution resolution,
        double? customHours = null,
        CancellationToken cancellationToken = default);
}
