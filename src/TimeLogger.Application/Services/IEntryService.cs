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
    string? MetadataJson);

public interface IEntryService
{
    Task<IReadOnlyList<EntryListItem>> GetUnmappedAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task<int> GetUnmappedCountAsync(string? accountIdFilter = null, CancellationToken ct = default);
    Task ManualMapAsync(int entryId, int timelogProjectId, int? timelogTaskId, CancellationToken ct = default);
    Task IgnoreAsync(int entryId, CancellationToken ct = default);
    Task<IReadOnlyList<EntryListItem>> GetAllAsync(int page, int pageSize, string? accountIdFilter = null, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(string? accountIdFilter = null, CancellationToken ct = default);
}
