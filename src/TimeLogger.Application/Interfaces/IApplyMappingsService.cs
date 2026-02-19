using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Interfaces;

public interface IApplyMappingsService
{
    /// <summary>
    /// Applies all enabled mapping rules to every <see cref="ImportStatus.Pending"/> entry.
    /// Matched entries are updated to <see cref="Domain.ImportStatus.Mapped"/> and assigned
    /// a <see cref="ImportedEntry.TimelogProjectId"/> (and optionally <see cref="ImportedEntry.TimelogTaskId"/>).
    /// </summary>
    /// <returns>Number of entries that were newly mapped.</returns>
    Task<int> ApplyAllPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the IDs of all <see cref="ImportStatus.Pending"/> entries that the given rule would match,
    /// without persisting any changes. Used for 'Test Rule' previews in the UI.
    /// </summary>
    Task<IReadOnlyList<ImportedEntry>> TestRuleAsync(int ruleId, CancellationToken cancellationToken = default);
}
