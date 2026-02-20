using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public record ImportSourceDto(
    int Id,
    string Name,
    SourceType SourceType,
    string? BaseUrl,
    string? PollSchedule,
    bool IsEnabled,
    DateTimeOffset? LastPolledAt,
    int TotalEntries,
    int PendingEntries);

public record ImportHistoryEntry(
    DateOnly WorkDate,
    string SourceName,
    int Total,
    int Pending,
    int Mapped,
    int Submitted,
    int Failed);

public interface IImportSourceService
{
    Task<IReadOnlyList<ImportSourceDto>> GetAllAsync(CancellationToken ct = default);
    Task<ImportSource> CreateAsync(string name, SourceType sourceType, string? baseUrl, string? apiToken, string? schedule, CancellationToken ct = default);
    Task UpdateAsync(int id, string name, string? baseUrl, string? apiToken, string? schedule, bool isEnabled, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ImportHistoryEntry>> GetImportHistoryAsync(int days = 30, CancellationToken ct = default);
    Task TriggerPollAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ImportSourceDto>> GetFileUploadSourcesAsync(CancellationToken ct = default);
}
