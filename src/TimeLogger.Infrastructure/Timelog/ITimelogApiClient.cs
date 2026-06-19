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

    /// <summary>Returns all employees (up to 500 per call).</summary>
    [Get("/v1/user?$pagesize=500")]
    Task<TafListResponse<TimelogUserDto>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing time registration in Timelog. The registration is identified by the ID (GUID) in the body.</summary>
    [Put("/v1/time-registration")]
    Task<IApiResponse> UpdateTimeRegistrationAsync(
        [Body] CreateTimeRegistrationDto model,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all time tracking items for a given date range, including manually-entered registrations.</summary>
    [Get("/v1/time-tracking-item/get-by-date")]
    Task<TafListResponse<TimeTrackingItemDto>> GetTimeTrackingItemsByDateAsync(
        [Query] string startDate,
        [Query] string endDate,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a time registration by its server-assigned integer ID.</summary>
    [Delete("/v1/time-registration/{id}")]
    Task<IApiResponse> DeleteTimeRegistrationAsync(
        int id,
        CancellationToken cancellationToken = default);
}
