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

        var employeeMapping = await db.EmployeeMappings
            .FirstOrDefaultAsync(m => m.AtlassianAccountId == entry.UserEmail, cancellationToken);

        if (task.ApiTaskId is null)
        {
            logger.LogError(
                "TimelogTask {TaskId} has no ApiTaskId — re-sync Timelog data and retry (entry {EntryId})",
                task.Id, entry.Id);
            return;
        }

        var comment = entry.Description;
        if (entry.MappingRuleId.HasValue)
        {
            var rule = await db.MappingRules.FindAsync([entry.MappingRuleId.Value], cancellationToken);
            if (rule?.IncludeIssueKeyInComment == true && !string.IsNullOrWhiteSpace(entry.IssueKey))
                comment = $"{entry.IssueKey} - {comment}";
        }

        var existingSubmission = await db.SubmittedEntries
            .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

        // Reuse the same client-side GUID across retries so Timelog can deduplicate.
        var clientId = existingSubmission?.ExternalId is { } stored
            ? Guid.Parse(stored)
            : Guid.NewGuid();

        var model = new CreateTimeRegistrationDto
        {
            Id = clientId,
            TaskId = task.ApiTaskId.Value,
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
            Hours = Math.Round(entry.TimeSpentSeconds / 3600.0, 2),
            Comment = comment,
            Billable = false,
            UserId = employeeMapping?.TimelogUserId,
        };

        var attemptCount = (existingSubmission?.AttemptCount ?? 0) + 1;

        try
        {
            var response = await apiClient.CreateTimeRegistrationAsync(model, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Submitted entry {EntryId} to Timelog (task {TaskExternalId}, clientId {ClientId})",
                    entry.Id, task.ExternalId, clientId);

                entry.Status = ImportStatus.Submitted;

                if (existingSubmission is null)
                {
                    db.SubmittedEntries.Add(new SubmittedEntry
                    {
                        ImportedEntryId = entry.Id,
                        ExternalId = clientId.ToString(),
                        Status = SubmissionStatus.Success,
                        SubmittedAt = DateTimeOffset.UtcNow,
                        AttemptCount = attemptCount,
                    });
                }
                else
                {
                    existingSubmission.ExternalId = clientId.ToString();
                    existingSubmission.Status = SubmissionStatus.Success;
                    existingSubmission.SubmittedAt = DateTimeOffset.UtcNow;
                    existingSubmission.AttemptCount = attemptCount;
                    existingSubmission.ErrorMessage = null;
                }
            }
            else
            {
                var error = response.Error?.Content ?? response.Error?.Message;
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
