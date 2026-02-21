using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Timelog;

public class TimelogSyncService(
    ITimelogApiClient apiClient,
    AppDbContext db,
    ILogger<TimelogSyncService> logger) : ITimelogSyncService
{
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting Timelog project/task sync");

        var projectDtos = await FetchAllProjectsAsync(cancellationToken);
        logger.LogInformation("Fetched {Count} projects from Timelog", projectDtos.Count);

        var syncedAt = DateTimeOffset.UtcNow;

        foreach (var dto in projectDtos)
        {
            var externalId = dto.ProjectId.ToString();

            var project = await db.TimelogProjects
                .FirstOrDefaultAsync(p => p.ExternalId == externalId, cancellationToken);

            if (project is null)
            {
                project = new TimelogProject
                {
                    ExternalId = externalId,
                    Name = dto.Name,
                    Description = dto.Description,
                    IsActive = true,
                    LastSyncedAt = syncedAt,
                };
                db.TimelogProjects.Add(project);
            }
            else
            {
                project.Name = dto.Name;
                project.Description = dto.Description;
                project.IsActive = true;
                project.LastSyncedAt = syncedAt;
            }

            await db.SaveChangesAsync(cancellationToken);

            await SyncTasksForProjectAsync(project, dto.ProjectId, syncedAt, cancellationToken);
        }

        // Mark projects no longer returned as inactive
        var activeExternalIds = projectDtos.Select(p => p.ProjectId.ToString()).ToHashSet();
        var staleProjects = await db.TimelogProjects
            .Where(p => p.IsActive && !activeExternalIds.Contains(p.ExternalId))
            .ToListAsync(cancellationToken);

        foreach (var stale in staleProjects)
        {
            stale.IsActive = false;
            stale.LastSyncedAt = syncedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Timelog sync complete. {ProjectCount} projects, {StaleCount} marked inactive",
            projectDtos.Count, staleProjects.Count);
    }

    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken cancellationToken = default)
    {
        return await db.TimelogProjects
            .OrderByDescending(p => p.LastSyncedAt)
            .Select(p => (DateTimeOffset?)p.LastSyncedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task SyncTasksForProjectAsync(
        TimelogProject project,
        int externalProjectId,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken)
    {
        var response = await apiClient.GetTasksByProjectIdAsync(externalProjectId, cancellationToken: cancellationToken);
        var taskDtos = response.Data;

        var existingTasks = await db.TimelogTasks
            .Where(t => t.TimelogProjectId == project.Id)
            .ToListAsync(cancellationToken);

        foreach (var dto in taskDtos)
        {
            var externalId = dto.TaskId.ToString();
            var task = existingTasks.FirstOrDefault(t => t.ExternalId == externalId);

            if (task is null)
            {
                task = new TimelogTask
                {
                    ExternalId = externalId,
                    Name = dto.Name,
                    IsActive = dto.IsActive,
                    LastSyncedAt = syncedAt,
                    TimelogProjectId = project.Id,
                };
                db.TimelogTasks.Add(task);
            }
            else
            {
                task.Name = dto.Name;
                task.IsActive = dto.IsActive;
                task.LastSyncedAt = syncedAt;
            }
        }

        // Mark tasks no longer returned as inactive
        var activeTaskIds = taskDtos.Select(t => t.TaskId.ToString()).ToHashSet();
        foreach (var stale in existingTasks.Where(t => t.IsActive && !activeTaskIds.Contains(t.ExternalId)))
        {
            stale.IsActive = false;
            stale.LastSyncedAt = syncedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Dto.TimelogProjectDto>> FetchAllProjectsAsync(CancellationToken cancellationToken)
    {
        var response = await apiClient.GetProjectsAsync(isActive: true, cancellationToken: cancellationToken);
        return response.Data;
    }
}
