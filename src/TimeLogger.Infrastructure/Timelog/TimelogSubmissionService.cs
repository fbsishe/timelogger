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

        // Pre-flight: check our own SubmittedEntries for a previous successful submission to the
        // same task/user/date. The Timelog API has no list/filter endpoint for time registrations,
        // so local-DB history is the only source of truth we have.
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
                    "Entry {EntryId} matches a previously submitted entry ({PrevId}) with identical hours ({Hours}h) — marking as Duplicate",
                    entry.Id, previousSubmission.ImportedEntryId, ourHours);

                entry.Status = ImportStatus.Submitted;
                var existingAudit = await db.SubmittedEntries
                    .FirstOrDefaultAsync(s => s.ImportedEntryId == entry.Id, cancellationToken);

                if (existingAudit is null)
                {
                    db.SubmittedEntries.Add(new SubmittedEntry
                    {
                        ImportedEntryId = entry.Id,
                        ExternalId = previousSubmission.ExternalId,
                        Status = SubmissionStatus.Duplicate,
                        SubmittedAt = DateTimeOffset.UtcNow,
                        AttemptCount = 1,
                    });
                }
                else
                {
                    existingAudit.Status = SubmissionStatus.Duplicate;
                    existingAudit.SubmittedAt = DateTimeOffset.UtcNow;
                    existingAudit.ExternalId = previousSubmission.ExternalId;
                    existingAudit.ErrorMessage = null;
                }

                await db.SaveChangesAsync(cancellationToken);
                return SubmitOutcome.Duplicate;
            }
            else
            {
                logger.LogInformation(
                    "Entry {EntryId} conflicts with previously submitted entry ({PrevId}): ours {Ours}h, previous {Previous}h — flagging for user resolution",
                    entry.Id, previousSubmission.ImportedEntryId, ourHours, previousHours);

                entry.Status = ImportStatus.Conflict;
                entry.ConflictHoursInTimelog = previousHours;
                entry.ConflictTimelogRegistrationId = previousSubmission.ExternalId;
                await db.SaveChangesAsync(cancellationToken);
                return SubmitOutcome.Conflict;
            }
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

        var clientId = existingSubmission?.ExternalId is { } stored
            ? Guid.Parse(stored)
            : Guid.NewGuid();

        var model = new CreateTimeRegistrationDto
        {
            Id = clientId,
            TaskId = resolvedApiTaskId,
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
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

        if (!Guid.TryParse(entry.ConflictTimelogRegistrationId, out var registrationGuid))
        {
            logger.LogError("Entry {EntryId} has an invalid ConflictTimelogRegistrationId '{Id}' — cannot resolve",
                entry.Id, entry.ConflictTimelogRegistrationId);
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

        // PUT /v1/time-registration — the registration is identified by the ID in the body.
        var model = new CreateTimeRegistrationDto
        {
            Id = registrationGuid,
            TaskId = resolvedApiTaskId,
            Date = entry.WorkDate.ToString("yyyy-MM-dd"),
            Hours = targetHours,
            Comment = comment,
            Billable = false,
            UserId = employeeMapping?.TimelogUserId,
        };

        try
        {
            var response = await apiClient.UpdateTimeRegistrationAsync(model, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Resolved conflict for entry {EntryId} via {Resolution} — updated Timelog registration {RegId} to {Hours}h",
                    entry.Id, resolution, entry.ConflictTimelogRegistrationId, targetHours);

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
                        ExternalId = registrationGuid.ToString(),
                        Status = SubmissionStatus.Success,
                        SubmittedAt = DateTimeOffset.UtcNow,
                        AttemptCount = 1,
                    });
                }
                else
                {
                    existingAudit.Status = SubmissionStatus.Success;
                    existingAudit.SubmittedAt = DateTimeOffset.UtcNow;
                    existingAudit.ExternalId = registrationGuid.ToString();
                    existingAudit.ErrorMessage = null;
                }

                await db.SaveChangesAsync(cancellationToken);
                return SubmitOutcome.Succeeded;
            }
            else
            {
                var error = response.Error?.Content ?? response.Error?.Message;
                logger.LogWarning(
                    "Timelog rejected conflict resolution for entry {EntryId}: {StatusCode} — {Error}",
                    entry.Id, response.StatusCode, error);
                return SubmitOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception resolving conflict for entry {EntryId}", entry.Id);
            return SubmitOutcome.Failed;
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
