using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public record TimelogProjectSummary(
    int Id,
    string ExternalId,
    string Name,
    int TaskCount,
    bool IsActive,
    DateTimeOffset LastSyncedAt);

public record TimelogTaskSummary(
    int Id,
    string ExternalId,
    string Name,
    bool IsActive);

public interface ITimelogDataService
{
    Task<IReadOnlyList<TimelogProjectSummary>> GetProjectsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TimelogTaskSummary>> GetTasksByProjectAsync(int projectId, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default);
    Task TriggerSyncAsync(CancellationToken ct = default);
}
