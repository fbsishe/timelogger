using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Timelog;

public class DayReviewService(
    ITimelogApiClient apiClient,
    ITimelogReportingClient reportingClient,
    AppDbContext db,
    ITimelogSubmissionService submitter,
    ILogger<DayReviewService> logger) : IDayReviewService
{
    public async Task<DayReviewResult> GetDayReviewAsync(
        string accountId, DateOnly date, CancellationToken ct = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => e.UserEmail == accountId && e.WorkDate == date && e.Status != ImportStatus.Ignored)
            .Include(e => e.TimelogTask)
            .OrderBy(e => e.IssueKey)
            .ToListAsync(ct);

        var mapping = await db.EmployeeMappings
            .FirstOrDefaultAsync(m => m.AtlassianAccountId == accountId, ct);

        var userDisplay = mapping?.DisplayName ?? mapping?.TimelogUserDisplayName ?? accountId;

        List<(int TaskId, DayReviewRegistration Registration)> registrations = [];
        var timelogQueried = false;
        string? warning = null;
        string? timesheetStatus = null;

        if (mapping is null)
        {
            warning = "No employee mapping exists for this person — Timelog cannot be queried.";
        }
        else
        {
            var dateStr = date.ToString("yyyy-MM-dd");

            try
            {
                var statuses = await apiClient.GetWeeklyTimesheetStatusAsync(
                    $"{dateStr}T00:00:00", $"{dateStr}T23:59:59", mapping.TimelogUserId, ct);
                timesheetStatus = statuses?.Data?
                    .FirstOrDefault(s => s.EmployeeUserId == mapping.TimelogUserId)?
                    .Details.FirstOrDefault()?.TimesheetStatus;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch Timelog timesheet status for {AccountId} on {Date}", accountId, date);
            }

            if (reportingClient.IsConfigured)
            {
                // The Reporting API sees every employee's registrations.
                try
                {
                    registrations = (await reportingClient.GetWorkUnitsAsync(date, date, ct))
                        .Where(u => u.UserId == mapping.TimelogUserId)
                        .Select(u => (u.TaskId, new DayReviewRegistration(
                            u.TimeRegistrationGuid ?? "", u.TaskName, u.ProjectName, u.Hours,
                            u.Note, u.ApprovedStatus, u.Invoiced, u.Created, u.LastModified)))
                        .ToList();
                    timelogQueried = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Reporting API query failed for {AccountId} on {Date}", accountId, date);
                    warning = $"Timelog could not be queried: {ex.Message}";
                }
            }
            else
            {
                // REST fallback: get-by-date only ever returns registrations of the user the API
                // key is issued to, so for anyone else an empty result means "not visible".
                int? apiUserId = null;
                try
                {
                    apiUserId = (await apiClient.GetCurrentUserAsync(ct))?.Properties?.UserId;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to resolve the Timelog API key user");
                }

                if (apiUserId != mapping.TimelogUserId)
                {
                    warning = $"Timelog registrations for {userDisplay} cannot be read with the current credentials " +
                              "(no Reporting API configured, and the REST key only sees its own user) — " +
                              "the Timelog side is unknown.";
                }
                else
                {
                    try
                    {
                        var items = await apiClient.GetTimeTrackingItemsByDateAsync(
                            $"{dateStr}T00:00:00", $"{dateStr}T23:59:59", ct);

                        registrations = items?.Data?
                            .Where(t => t.UserId == mapping.TimelogUserId)
                            .Select(t => (t.TaskId, new DayReviewRegistration(
                                t.TimeRegistrationId.ToString(), t.TaskName, t.ProjectName, t.Hours,
                                t.Comment, t.ApprovalStatus, t.InvoiceStatus, t.Created, t.LastModified)))
                            .ToList() ?? [];
                        timelogQueried = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to fetch Timelog registrations for {AccountId} on {Date}", accountId, date);
                        warning = $"Timelog could not be queried: {ex.Message}";
                    }
                }
            }
        }

        return new DayReviewResult(userDisplay, date, timelogQueried, warning,
            BuildGroups(entries, registrations, timelogQueried), timesheetStatus);
    }

    private static List<DayReviewGroup> BuildGroups(
        List<ImportedEntry> entries,
        List<(int TaskId, DayReviewRegistration Registration)> registrations,
        bool timelogQueried)
    {
        var mapped = entries.Where(e => ResolveApiTaskId(e) is not null).ToList();
        var unmapped = entries.Except(mapped).ToList();

        var oursByTask = mapped
            .GroupBy(e => ResolveApiTaskId(e)!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var regsByTask = registrations
            .GroupBy(r => r.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Registration).ToList());

        var groups = new List<DayReviewGroup>();

        foreach (var taskId in oursByTask.Keys.Union(regsByTask.Keys).Order())
        {
            var ours = oursByTask.GetValueOrDefault(taskId, []);
            var regs = regsByTask.GetValueOrDefault(taskId, []);

            var taskName = ours.FirstOrDefault()?.TimelogTask?.Name
                ?? regs.FirstOrDefault()?.TaskName
                ?? $"Task {taskId}";

            var ourHours = Math.Round(ours.Sum(e => e.TimeSpentSeconds / 3600.0), 2);
            var timelogHours = Math.Round(regs.Sum(r => r.Hours), 2);

            var state = (ours.Count, regs.Count) switch
            {
                ( > 0, 0) => DayGroupState.MissingInTimelog,
                (0, > 0) => DayGroupState.OnlyInTimelog,
                _ => Math.Abs(ourHours - timelogHours) < 0.01 ? DayGroupState.Match : DayGroupState.Different,
            };

            // Without a successful Timelog query, an empty Timelog side means "unknown", not "missing".
            if (!timelogQueried && state == DayGroupState.MissingInTimelog)
                state = DayGroupState.Unmapped;

            groups.Add(new DayReviewGroup(taskId, taskName, ToEntryItems(ours), regs, state));
        }

        if (unmapped.Count > 0)
        {
            groups.Add(new DayReviewGroup(
                null, "(not mapped to a Timelog task)", ToEntryItems(unmapped), [], DayGroupState.Unmapped));
        }

        return groups;
    }

    private static List<DayReviewEntry> ToEntryItems(List<ImportedEntry> entries) =>
        entries.Select(e => new DayReviewEntry(
            e.Id,
            e.IssueKey,
            e.Description,
            Math.Round(e.TimeSpentSeconds / 3600.0, 2),
            e.Status.ToString())).ToList();

    // Mirrors the task-ID resolution used at submission time.
    private static int? ResolveApiTaskId(ImportedEntry entry)
    {
        if (entry.TimelogTask?.ApiTaskId is { } apiId) return apiId;
        if (int.TryParse(entry.TimelogTask?.ExternalId, out var parsed) && parsed > 0) return parsed;
        return null;
    }

    public async Task<SubmitOutcome> ResolveDifferenceAsync(
        int entryId,
        string timelogRegistrationId,
        double timelogHours,
        ConflictResolution resolution,
        double? customHours = null,
        CancellationToken ct = default)
    {
        var entry = await db.ImportedEntries
            .Include(e => e.TimelogTask)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException($"Entry {entryId} not found.");

        // Record the live registration as a conflict first: if the resolution fails midway,
        // the entry surfaces on the submission page instead of silently staying "Submitted".
        entry.Status = ImportStatus.Conflict;
        entry.ConflictHoursInTimelog = timelogHours;
        entry.ConflictTimelogRegistrationId = timelogRegistrationId;
        await db.SaveChangesAsync(ct);

        return await submitter.ResolveConflictAsync(entry, resolution, customHours, ct);
    }
}
