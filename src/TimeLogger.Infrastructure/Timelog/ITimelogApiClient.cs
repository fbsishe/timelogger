using Refit;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Timelog;

[Headers("Authorization: Bearer")]
public interface ITimelogApiClient
{
    /// <summary>Returns all active projects. Optionally filter by customer.</summary>
    [Get("/project/GetAll")]
    Task<TafListResponse<TimelogProjectDto>> GetProjectsAsync(
        [Query] bool isActive = true,
        [Query] string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all tasks for a given Timelog project (integer ID).</summary>
    [Get("/task/GetAllByProjectID")]
    Task<TafListResponse<TimelogTaskDto>> GetTasksByProjectIdAsync(
        [Query] int projectID,
        [Query] string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a time registration in Timelog.</summary>
    [Post("/timeregistration/Create")]
    Task<IApiResponse> CreateTimeRegistrationAsync(
        [Body] CreateTimeRegistrationDto model,
        CancellationToken cancellationToken = default);
}
