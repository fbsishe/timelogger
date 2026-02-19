using Refit;
using TimeLogger.Infrastructure.Tempo.Dto;

namespace TimeLogger.Infrastructure.Tempo;

[Headers("Authorization: Bearer")]
public interface ITempoApiClient
{
    /// <summary>
    /// Returns worklogs within a date range with pagination.
    /// The token for a specific ImportSource is injected per-call via the header override.
    /// </summary>
    [Get("/worklogs")]
    Task<TempoPagedResponse<TempoWorklogDto>> GetWorklogsAsync(
        [Query] string from,
        [Query] string to,
        [Query] int offset = 0,
        [Query] int limit = 5000,
        CancellationToken cancellationToken = default);
}
