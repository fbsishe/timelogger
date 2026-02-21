using Refit;
using TimeLogger.Infrastructure.Timelog.Dto;


namespace TimeLogger.Infrastructure.Timelog;

[Headers("Authorization: Bearer")]
public interface ITimelogApiClient
{
    /// <summary>Returns all projects (up to 500 per call).</summary>
    [Get("/v1/project/get-all?$pagesize=500")]
    Task<TafListResponse<TimelogProjectDto>> GetProjectsAsync(
        [Query] bool isActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>Returns tasks for a given project (up to 500 per call).</summary>
    [Get("/v1/task/filter?$pagesize=500")]
    Task<TafListResponse<TimelogTaskDto>> GetTasksByProjectIdAsync(
        [Query] int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a time registration in Timelog.</summary>
    [Post("/v1/time-registration")]
    Task<IApiResponse> CreateTimeRegistrationAsync(
        [Body] CreateTimeRegistrationDto model,
        CancellationToken cancellationToken = default);
}
