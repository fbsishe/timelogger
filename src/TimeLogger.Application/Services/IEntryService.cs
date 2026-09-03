using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public record EntryListItem(
    int Id,
    string ExternalId,
    string SourceName,
    DateOnly WorkDate,
    double Hours,
    string? ProjectKey,
    string? IssueKey,
    string? Description,
    string? UserEmail,
    string Status,
    string? MetadataJson,
    string? RawUserEmail = null,
    DateTimeOffset? AmendedAfterSubmissionAt = null,
    double? AmendedSourceHours = null,
    string? AmendedSourceDescription = null)
{
    /// <summary>The source changed this entry after we pushed it to Timelog; needs a human.</summary>
    public bool IsAmendedAfterSubmission => AmendedAfterSubmissionAt is not null;

    /// <summary>Signed hour difference between the source's current value and what we submitted.</summary>
    public double? AmendedHoursDelta =>
        AmendedSourceHours is { } amended ? Math.Round(amended - Hours, 2) : null;
}

public interface IEntryService
{
    Task<IReadOnlyList<EntryListItem>> GetUnmappedAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task<int> GetUnmappedCountAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task ManualMapAsync(int entryId, int timelogProjectId, int? timelogTaskId, CancellationToken ct = default);
    Task IgnoreAsync(int entryId, CancellationToken ct = default);
    Task<IReadOnlyList<EntryListItem>> GetAllAsync(int page, int pageSize, string? accountIdFilter = null, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(string? accountIdFilter = null, CancellationToken ct = default);

    /// <summary>Entries the source amended after we submitted them to Timelog.</summary>
    Task<IReadOnlyList<EntryListItem>> GetAmendedAfterSubmissionAsync(
        string? accountIdFilter = null, CancellationToken ct = default);

    Task<int> GetAmendedAfterSubmissionCountAsync(
        string? accountIdFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Dismisses the amendment flag on an entry. The source's timestamp is recorded as seen, so
    /// the next pull stops re-flagging it — use once the Timelog registration has been squared up.
    /// </summary>
    Task AcknowledgeAmendmentAsync(int entryId, CancellationToken ct = default);
}
