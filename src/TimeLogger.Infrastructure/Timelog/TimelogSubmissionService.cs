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
    public async Task<SubmitOutcome> SubmitAsync(ImportedEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.TimelogTaskId is null)
        {
            logger.LogWarning("Entry {EntryId} has no mapped Timelog task — skipping submission", entry.Id);
            return SubmitOutcome.Skipped;
        }

        var task = await db.TimelogTasks.FindAsync([entry.TimelogTaskId.Value], cancellationToken);
        if (task is null)
        {
            logger.LogError("TimelogTask {TaskId} not found for entry {EntryId}", entry.TimelogTaskId, entry.Id);
            return SubmitOutcome.Skipped;
        }

        var employeeMapping = await db.EmployeeMappings
            .FirstOrDefaultAsync(m => m.AtlassianAccountId == entry.UserEmail, cancellationToken);

        int resolvedApiTaskId;
        if (task.ApiTaskId is not null)
        {
            resolvedApiTaskId = task.ApiTaskId.Value;
        }
        else if (int.TryParse(task.ExternalId, out var parsedId) && parsedId > 0)
        {
            logger.LogWarning(
                "TimelogTask {TaskId} has no ApiTaskId; using ExternalId {ExternalId} as fallback — run a sync to fix permanently",
                task.Id, task.ExternalId);
            resolvedApiTaskId = parsedId;
        }
        else
        {
            logger.LogError(
                "TimelogTask {TaskId} has no ApiTaskId and ExternalId is not numeric — re-sync Timelog data and retry (entry {EntryId})",
                task.Id, entry.Id);
            return SubmitOutcome.Skipped;
        }

        var ourHours = Math.Round(entry.TimeSpentSeconds / 3600.0, 2);
        var dateStr = entry.WorkDate.ToString("yyyy-MM-dd");

        // Primary conflict check: query Timelog directly for all registrations on this date.
        // This catches manually-entered entries that our local DB doesn't know about.
        var performedApiCheck = false;
        if (employeeMapping is not null)
        {
            try
            {
                var items = await apiClient.GetTimeTrackingItemsByDateAsync(
                    $"{dateStr}T00:00:00",
                    $"{dateStr}T23:59:59",
                    cancellationToken);

                var existing = items?.Data?.FirstOrDefault(t =>
                    t.TaskId == resolvedApiTaskId && t.UserId == employeeMapping.TimelogUserId);

                performedApiCheck = true;

                if (existing is not null)
                {
                    if (Math.Abs(existing.Hours - ourHours) < 0.01)
                    {
                        logger.LogInformation(
                            "Entry {EntryId}: Timelog already has {Hours}h for task {TaskId} on {Date} — Duplicate",
                            entry.Id, existing.Hours, resolvedApiTaskId, dateStr);
                        return await RecordDuplicate(entry, existing.TimeRegistrationId.ToString(), cancellationToken);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Entry {EntryId}: Timelog has {ExistingHours}h for task {TaskId} on {Date}, we have {OurHours}h — Conflict",
                            entry.Id, existing.Hours, resolvedApiTaskId, dateStr, ourHours);
                        return await RecordConflict(entry, existing.Hours, existing.TimeRegistrationId.ToString(), cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Timelog conflict check failed for entry {EntryId}; falling back to local-DB check",
                    entry.Id);
            }
        }

        // Fallback: check our own SubmittedEntries for a previous successful submission to the
        // same task/user/date. Only runs when the API check above was skipped or failed.
        if (!performedApiCheck)
        {
            var previousSubmission = await db.SubmittedEntries
                .Include(s => s.ImportedEntry)
                .Where(s => s.Status == SubmissionStatus.Success
                    && s.ImportedEntryId != entry.Id
                    && s.ImportedEntry.TimelogTaskId == entry.TimelogTaskId
                    && s.ImportedEntry.UserEmail == entry.UserEmail
                    && s.ImportedEntry.WorkDate == entry.WorkDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (previousSubmission is not null)
            {
                var previousHours = Math.Round(previousSubmission.ImportedEntry.TimeSpentSeconds / 3600.0, 2);

                if (Math.Abs(previousHours - ourHours) < 0.01)
                {
                    logger.LogInformation(
                        "Entry {EntryId} matches previously submitted entry ({PrevId}) with {Hours}h — Duplicate (local fallback)",
                        entry.Id, previousSubmission.ImportedEntryId, ourHours);
                    return await RecordDuplicate(entry, previousSubmission.ExternalId, cancellationToken);
                }
                else
                {
                    logger.LogInformation(
                        "Entry {EntryId} conflicts with previously submitted entry ({PrevId}): ours {Ours}h, theirs {Previous}h — Conflict (local fallback)",
                        entry.Id, previousSubmission.ImportedEntryId, ourHours, previousHours);
                    return await RecordConflict(entry, previousHours, previousSubmission.ExternalId, cancellationToken);
                }
            }
        }

        // No conflict detected — proceed with normal POST.
        var comment = entry.Description;
        if (entry.MappingRuleId.HasValue)
        {
            var rule = await db.MappingRules.FindAsync([entry.MappingRuleId.Value], cancellationToken);
            if (rule?.IncludeIssueKeyInComment == true && !string.IsNullOrWhiteSpace(entry.IssueKey))
                comment = $"{entry.IssueKey} - {comment}";
        }

        var existingSubmission = await db.SubmittedEntries
            .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

        var clientId = existingSubmission?.ExternalId is { } stored && Guid.TryParse(stored, out var g)
            ? g
            : Guid.NewGuid();

        var model = new CreateTimeRegistrationDto
        {
            Id = clientId,
            TaskId = resolvedApiTaskId,
            Date = dateStr,
            Hours = ourHours,
            Comment = comment,
            Billable = false,
            UserId = employeeMapping?.TimelogUserId,
        };

        var attemptCount = (existingSubmission?.AttemptCount ?? 0) + 1;

        SubmitOutcome outcome;

        try
        {
            var response = await apiClient.CreateTimeRegistrationAsync(model, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Submitted entry {EntryId} to Timelog (task {TaskExternalId}, clientId {ClientId})",
                    entry.Id, task.ExternalId, clientId);

                entry.Status = ImportStatus.Submitted;
                outcome = SubmitOutcome.Succeeded;

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
                outcome = SubmitOutcome.Failed;
                PersistFailure(db, existingSubmission, entry.Id, attemptCount, $"{(int)response.StatusCode}: {error}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception submitting entry {EntryId} to Timelog", entry.Id);
            entry.Status = ImportStatus.Failed;
            outcome = SubmitOutcome.Failed;
            PersistFailure(db, existingSubmission, entry.Id, attemptCount, ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return outcome;
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

    public async Task<SubmitOutcome> ResolveConflictAsync(
        ImportedEntry entry,
        ConflictResolution resolution,
        double? customHours = null,
        CancellationToken cancellationToken = default)
    {
        if (entry.ConflictTimelogRegistrationId is null)
        {
            logger.LogError("Entry {EntryId} has no ConflictTimelogRegistrationId — cannot resolve", entry.Id);
            return SubmitOutcome.Skipped;
        }

        var task = entry.TimelogTaskId.HasValue
            ? await db.TimelogTasks.FindAsync([entry.TimelogTaskId.Value], cancellationToken)
            : null;

        var employeeMapping = await db.EmployeeMappings
            .FirstOrDefaultAsync(m => m.AtlassianAccountId == entry.UserEmail, cancellationToken);

        int resolvedApiTaskId = 0;
        if (task?.ApiTaskId is not null)
            resolvedApiTaskId = task.ApiTaskId.Value;
        else if (task is not null && int.TryParse(task.ExternalId, out var parsedId) && parsedId > 0)
            resolvedApiTaskId = parsedId;

        var ourHours = Math.Round(entry.TimeSpentSeconds / 3600.0, 2);
        var targetHours = resolution switch
        {
            ConflictResolution.UseOurs       => ourHours,
            ConflictResolution.AddToExisting => Math.Round(ourHours + (entry.ConflictHoursInTimelog ?? 0), 2),
            ConflictResolution.SetCustom     => Math.Round(customHours ?? ourHours, 2),
            _                                => ourHours,
        };

        var comment = entry.Description;
        if (entry.MappingRuleId.HasValue)
        {
            var rule = await db.MappingRules.FindAsync([entry.MappingRuleId.Value], cancellationToken);
            if (rule?.IncludeIssueKeyInComment == true && !string.IsNullOrWhiteSpace(entry.IssueKey))
                comment = $"{entry.IssueKey} - {comment}";
        }

        var regId = entry.ConflictTimelogRegistrationId;

        // Integer ID → API-detected conflict (may be manually-entered). Delete and recreate.
        if (int.TryParse(regId, out var intRegId))
        {
            return await ResolveViaDeleteAndCreate(entry, intRegId, resolvedApiTaskId, targetHours, comment, employeeMapping, cancellationToken);
        }

        // GUID → from our local-DB fallback (a registration we previously created). Update in-place via PUT.
        if (Guid.TryParse(regId, out var guidRegId))
        {
            return await ResolveViaPut(entry, guidRegId, resolvedApiTaskId, targetHours, comment, employeeMapping, cancellationToken);
        }

        logger.LogError("Entry {EntryId} has an unrecognised ConflictTimelogRegistrationId '{Id}'", entry.Id, regId);
        return SubmitOutcome.Skipped;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<SubmitOutcome> RecordDuplicate(
        ImportedEntry entry,
        string? externalId,
        CancellationToken cancellationToken)
    {
        entry.Status = ImportStatus.Submitted;

        var existing = await db.SubmittedEntries
            .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

        if (existing is null)
        {
            db.SubmittedEntries.Add(new SubmittedEntry
            {
                ImportedEntryId = entry.Id,
                ExternalId = externalId,
                Status = SubmissionStatus.Duplicate,
                SubmittedAt = DateTimeOffset.UtcNow,
                AttemptCount = 1,
            });
        }
        else
        {
            existing.Status = SubmissionStatus.Duplicate;
            existing.SubmittedAt = DateTimeOffset.UtcNow;
            existing.ExternalId = externalId;
            existing.ErrorMessage = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return SubmitOutcome.Duplicate;
    }

    private async Task<SubmitOutcome> RecordConflict(
        ImportedEntry entry,
        double timelogHours,
        string? timelogRegistrationId,
        CancellationToken cancellationToken)
    {
        entry.Status = ImportStatus.Conflict;
        entry.ConflictHoursInTimelog = timelogHours;
        entry.ConflictTimelogRegistrationId = timelogRegistrationId;
        await db.SaveChangesAsync(cancellationToken);
        return SubmitOutcome.Conflict;
    }

    private async Task<SubmitOutcome> ResolveViaDeleteAndCreate(
        ImportedEntry entry,
        int timelogRegistrationId,
        int apiTaskId,
        double targetHours,
        string? comment,
        EmployeeMapping? employeeMapping,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Resolving conflict for entry {EntryId}: deleting Timelog registration {RegId} then creating with {Hours}h",
            entry.Id, timelogRegistrationId, targetHours);

        var deleteResponse = await apiClient.DeleteTimeRegistrationAsync(timelogRegistrationId, cancellationToken);
        if (!deleteResponse.IsSuccessStatusCode)
        {
            var error = deleteResponse.Error?.Content ?? deleteResponse.Error?.Message;
            logger.LogWarning(
                "Failed to delete Timelog registration {RegId} for entry {EntryId}: {StatusCode} — {Error}",
                timelogRegistrationId, entry.Id, deleteResponse.StatusCode, error);
            return SubmitOutcome.Failed;
        }

        var newGuid = Guid.NewGuid();
        var model = new CreateTimeRegistrationDto
        {
            Id = newGuid,
            TaskId = apiTaskId,
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
            Hours = targetHours,
            Comment = comment,
            Billable = false,
            UserId = employeeMapping?.TimelogUserId,
        };

        var createResponse = await apiClient.CreateTimeRegistrationAsync(model, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            var error = createResponse.Error?.Content ?? createResponse.Error?.Message;
            logger.LogWarning(
                "Failed to create replacement registration for entry {EntryId}: {StatusCode} — {Error}",
                entry.Id, createResponse.StatusCode, error);
            return SubmitOutcome.Failed;
        }

        return await MarkConflictResolved(entry, newGuid.ToString(), cancellationToken);
    }

    private async Task<SubmitOutcome> ResolveViaPut(
        ImportedEntry entry,
        Guid registrationGuid,
        int apiTaskId,
        double targetHours,
        string? comment,
        EmployeeMapping? employeeMapping,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Resolving conflict for entry {EntryId}: updating Timelog registration {RegId} to {Hours}h via PUT",
            entry.Id, registrationGuid, targetHours);

        var model = new CreateTimeRegistrationDto
        {
            Id = registrationGuid,
            TaskId = apiTaskId,
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
            Hours = targetHours,
            Comment = comment,
            Billable = false,
            UserId = employeeMapping?.TimelogUserId,
        };

        var response = await apiClient.UpdateTimeRegistrationAsync(model, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = response.Error?.Content ?? response.Error?.Message;
            logger.LogWarning(
                "Failed to update Timelog registration {RegId} for entry {EntryId}: {StatusCode} — {Error}",
                registrationGuid, entry.Id, response.StatusCode, error);
            return SubmitOutcome.Failed;
        }

        return await MarkConflictResolved(entry, registrationGuid.ToString(), cancellationToken);
    }

    private async Task<SubmitOutcome> MarkConflictResolved(
        ImportedEntry entry,
        string externalId,
        CancellationToken cancellationToken)
    {
        entry.Status = ImportStatus.Submitted;
        entry.ConflictHoursInTimelog = null;
        entry.ConflictTimelogRegistrationId = null;

        var existingAudit = await db.SubmittedEntries
            .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

        if (existingAudit is null)
        {
            db.SubmittedEntries.Add(new SubmittedEntry
            {
                ImportedEntryId = entry.Id,
                ExternalId = externalId,
                Status = SubmissionStatus.Success,
                SubmittedAt = DateTimeOffset.UtcNow,
                AttemptCount = 1,
            });
        }
        else
        {
            existingAudit.Status = SubmissionStatus.Success;
            existingAudit.SubmittedAt = DateTimeOffset.UtcNow;
            existingAudit.ExternalId = externalId;
            existingAudit.ErrorMessage = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return SubmitOutcome.Succeeded;
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
