using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Timelog;

public class TimelogSubmissionService(
    ITimelogApiClient apiClient,
    AppDbContext db,
    ILogger<TimelogSubmissionService> logger) : ITimelogSubmissionService
{
    public async Task SubmitAsync(ImportedEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.TimelogTaskId is null)
        {
            logger.LogWarning("Entry {EntryId} has no mapped Timelog task — skipping submission", entry.Id);
            return;
        }

        var task = await db.TimelogTasks.FindAsync([entry.TimelogTaskId.Value], cancellationToken);
        if (task is null)
        {
            logger.LogError("TimelogTask {TaskId} not found for entry {EntryId}", entry.TimelogTaskId, entry.Id);
            return;
        }

        var model = new CreateTimeRegistrationDto
        {
            TaskId = int.Parse(task.ExternalId),
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
            Hours = Math.Round(entry.TimeSpentSeconds / 3600.0, 2),
            Comment = entry.Description,
            Billable = false,
        };

        var existingSubmission = await db.SubmittedEntries
            .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

        var attemptCount = (existingSubmission?.AttemptCount ?? 0) + 1;

        try
        {
            var response = await apiClient.CreateTimeRegistrationAsync(model, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Submitted entry {EntryId} to Timelog (task {TaskExternalId})",
                    entry.Id, task.ExternalId);

                entry.Status = ImportStatus.Submitted;

                if (existingSubmission is null)
                {
                    db.SubmittedEntries.Add(new SubmittedEntry
                    {
                        ImportedEntryId = entry.Id,
                        Status = SubmissionStatus.Success,
                        SubmittedAt = DateTimeOffset.UtcNow,
                        AttemptCount = attemptCount,
                    });
                }
                else
                {
                    existingSubmission.Status = SubmissionStatus.Success;
                    existingSubmission.SubmittedAt = DateTimeOffset.UtcNow;
                    existingSubmission.AttemptCount = attemptCount;
                    existingSubmission.ErrorMessage = null;
                }
            }
            else
            {
                var error = await response.Error?.GetContentAsAsync<string>()! ?? response.Error?.Message;
                logger.LogWarning("Timelog rejected entry {EntryId}: {StatusCode} — {Error}",
                    entry.Id, response.StatusCode, error);

                entry.Status = ImportStatus.Failed;
                PersistFailure(db, existingSubmission, entry.Id, attemptCount, $"{(int)response.StatusCode}: {error}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception submitting entry {EntryId} to Timelog", entry.Id);
            entry.Status = ImportStatus.Failed;
            PersistFailure(db, existingSubmission, entry.Id, attemptCount, ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAllPendingAsync(CancellationToken cancellationToken = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Mapped || e.Status == ImportStatus.Failed)
            .Where(e => e.TimelogTaskId != null)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Submitting {Count} pending entries to Timelog", entries.Count);

        foreach (var entry in entries)
        {
            await SubmitAsync(entry, cancellationToken);
        }
    }

    private static void PersistFailure(
        AppDbContext db,
        SubmittedEntry? existing,
        int entryId,
        int attemptCount,
        string errorMessage)
    {
        if (existing is null)
        {
            db.SubmittedEntries.Add(new SubmittedEntry
            {
                ImportedEntryId = entryId,
                Status = SubmissionStatus.Failed,
                SubmittedAt = DateTimeOffset.UtcNow,
                AttemptCount = attemptCount,
                ErrorMessage = errorMessage,
            });
        }
        else
        {
            existing.Status = SubmissionStatus.Failed;
            existing.SubmittedAt = DateTimeOffset.UtcNow;
            existing.AttemptCount = attemptCount;
            existing.ErrorMessage = errorMessage;
        }
    }
}
