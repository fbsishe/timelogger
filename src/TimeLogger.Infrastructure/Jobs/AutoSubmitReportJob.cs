using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job (production only, AutoSubmit:Enabled) — runs the whole chain on
/// every scheduled slot: pull worklogs from the source, apply mapping rules, submit all
/// non-problematic mapped entries to Timelog, then post a run report to Slack. Owning the
/// pull is the point: on a daily-only import, the 13:00 and 17:00 runs submitted whatever
/// the 06:00 pull happened to catch and silently ignored everything logged since.
///
/// The three steps are independent enough to be worth attempting individually — a Tempo
/// outage should not stop entries mapped on an earlier run from reaching Timelog — so each
/// one is guarded separately, every failure is named in the Slack report, and the run is
/// still recorded as failed so the dashboard and the failure notifier both see it.
///
/// Conflicting or unmapped entries are never auto-pushed; they are surfaced in the
/// report's "needs attention" section instead.
/// </summary>
public class AutoSubmitReportJob(
    AppDbContext db,
    ITempoImportService importService,
    IApplyMappingsService mappingService,
    ITimelogSubmissionService submissionService,
    IJobHealthService jobHealth,
    ISlackMessageSender slack,
    IOptions<AutoSubmitOptions> options,
    ILogger<AutoSubmitReportJob> logger)
{
    public const string JobId = "timelog-auto-submit";

    private const string PullStep = "Pull from Tempo";
    private const string MapStep = "Apply mapping rules";
    private const string SubmitStep = "Submit to Timelog";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var runStartUtc = DateTimeOffset.UtcNow;
        logger.LogInformation("AutoSubmitReportJob started at {Time}", runStartUtc);

        var failures = new List<StepFailure>();

        try
        {
            var lastRunUtc = await db.JobExecutions
                .Where(e => e.JobName == JobId)
                .OrderByDescending(e => e.ExecutedAt)
                .Select(e => (DateTimeOffset?)e.ExecutedAt)
                .FirstOrDefaultAsync(cancellationToken);

            // 1. Pull — everything created or amended in the source since its last poll.
            var pull = await RunStepAsync(
                PullStep, () => importService.ImportIncrementalAsync(cancellationToken), failures);

            // 2. Map — turn what we just pulled (plus anything still pending) into mapped entries.
            var mapped = await RunStepAsync(
                MapStep, () => mappingService.ApplyAllPendingAsync(cancellationToken), failures);

            // Counted after the pull so entries this run imported are "new since the last run".
            var newEntriesSinceLastRun = await db.ImportedEntries
                .CountAsync(e => e.ImportedAt > (lastRunUtc ?? DateTimeOffset.MinValue), cancellationToken);

            // 3. Submit — push the mapped, non-problematic entries to Timelog.
            await RunStepAsync(
                SubmitStep, () => submissionService.SubmitAllPendingAsync(cancellationToken), failures);

            var import = new ImportSummary(pull?.Imported ?? 0, pull?.Refreshed ?? 0, mapped);
            var localNow = TimeZoneInfo.ConvertTime(runStartUtc, ResolveTimeZone());
            var data = await CollectReportDataAsync(
                runStartUtc, localNow, newEntriesSinceLastRun, import, failures, cancellationToken);

            var anythingNew = newEntriesSinceLastRun > 0
                || data.Submitted.Count > 0
                || data.DuplicateCount > 0
                || data.FailedCount > 0
                || data.NewlyAmended.Count > 0;

            if (ShouldSendReport(localNow, anythingNew, failures.Count > 0))
            {
                var sent = await slack.SendAsync(SubmissionReportBuilder.Build(data), cancellationToken);
                logger.LogInformation("AutoSubmitReportJob report {Outcome}", sent ? "sent to Slack" : "NOT sent");

                // Only stamp them once the report is genuinely out, so a failed webhook
                // does not swallow the one mention each amendment gets.
                if (sent && data.NewlyAmended.Count > 0)
                    await MarkAmendmentsReportedAsync(data.NewlyAmended, cancellationToken);
            }
            else
            {
                logger.LogInformation("AutoSubmitReportJob: nothing new — report suppressed for this run");
            }

            if (failures.Count > 0)
            {
                // Reported to Slack above; record it here so the health dashboard and the
                // failure notifier see it too, then surface it to Hangfire for a retry.
                await jobHealth.RecordFailureAsync(JobId, DescribeFailures(failures), cancellationToken);
                throw new AutoSubmitStepFailedException(failures);
            }

            await jobHealth.RecordSuccessAsync(JobId, cancellationToken);
        }
        catch (AutoSubmitStepFailedException)
        {
            throw;   // already reported and recorded
        }
        catch (Exception ex)
        {
            // Something outside the three steps broke (reporting, the DB). The steps have no
            // say in this one, so Slack only hears about it through the failure notifier.
            logger.LogError(ex, "AutoSubmitReportJob failed");
            await jobHealth.RecordFailureAsync(JobId, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Runs one step, recording rather than propagating its failure so the remaining steps
    /// still get their turn. Returns default(T) when the step threw.
    /// </summary>
    private async Task<T?> RunStepAsync<T>(string step, Func<Task<T>> action, List<StepFailure> failures)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoSubmitReportJob step '{Step}' failed", step);
            failures.Add(new StepFailure(step, $"{ex.GetType().Name}: {ex.Message}"));
            return default;
        }
    }

    private async Task RunStepAsync(string step, Func<Task> action, List<StepFailure> failures) =>
        await RunStepAsync<object?>(step, async () => { await action(); return null; }, failures);

    private static string DescribeFailures(IReadOnlyList<StepFailure> failures) =>
        string.Join(" | ", failures.Select(f => $"{f.Step}: {f.Error}"));

    /// <summary>
    /// The 08:00 weekday run is an always-on heartbeat; every other run
    /// (13:00, 17:00, weekends) only reports when something new happened —
    /// or when a step failed, which is always worth saying out loud.
    /// </summary>
    public static bool ShouldSendReport(DateTimeOffset localNow, bool anythingNew, bool hasFailures = false)
    {
        if (hasFailures) return true;
        var isWeekday = localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        return (isWeekday && localNow.Hour == 8) || anythingNew;
    }

    private async Task<AutoSubmitReportData> CollectReportDataAsync(
        DateTimeOffset runStartUtc,
        DateTimeOffset localRunTime,
        int newEntriesSinceLastRun,
        ImportSummary import,
        IReadOnlyList<StepFailure> stepFailures,
        CancellationToken ct)
    {
        var runSubmissions = await db.SubmittedEntries
            .Where(s => s.SubmittedAt >= runStartUtc)
            .Include(s => s.ImportedEntry)
                .ThenInclude(e => e.TimelogProject)
            .ToListAsync(ct);

        var accountIds = runSubmissions
            .Select(s => s.ImportedEntry.UserEmail)
            .Where(e => e != null)
            .Distinct()
            .ToList();

        var names = accountIds.Count > 0
            ? await db.EmployeeMappings
                .Where(m => accountIds.Contains(m.AtlassianAccountId)
                            && (m.DisplayName != null || m.TimelogUserDisplayName != null))
                .ToDictionaryAsync(m => m.AtlassianAccountId,
                                   m => (m.DisplayName ?? m.TimelogUserDisplayName)!, ct)
            : [];

        var submitted = runSubmissions
            .Where(s => s.Status == SubmissionStatus.Success)
            .GroupBy(s => new
            {
                Employee = s.ImportedEntry.UserEmail is { } email && names.TryGetValue(email, out var name)
                    ? name
                    : s.ImportedEntry.UserEmail ?? "(unknown)",
                Project = s.ImportedEntry.TimelogProject?.Name ?? "(no project)",
            })
            .Select(g => new SubmittedGroup(
                g.Key.Employee,
                g.Key.Project,
                g.Count(),
                Math.Round(g.Sum(s => s.ImportedEntry.TimeSpentSeconds) / 3600.0, 2)))
            .ToList();

        var failures = runSubmissions.Where(s => s.Status == SubmissionStatus.Failed).ToList();

        // Amendments flagged by the Tempo pull that have not been announced yet.
        var amendedRows = await db.ImportedEntries
            .Where(e => e.AmendedAfterSubmissionAt != null && e.AmendmentReportedAt == null)
            .OrderByDescending(e => e.AmendedAfterSubmissionAt)
            .ToListAsync(ct);

        var amendedNames = amendedRows.Count > 0
            ? await db.EmployeeMappings
                .Where(m => amendedRows.Select(e => e.UserEmail).Contains(m.AtlassianAccountId)
                            && (m.DisplayName != null || m.TimelogUserDisplayName != null))
                .ToDictionaryAsync(m => m.AtlassianAccountId,
                                   m => (m.DisplayName ?? m.TimelogUserDisplayName)!, ct)
            : [];

        var newlyAmended = amendedRows
            .Select(e => new AmendedAfterSubmission(
                e.Id,
                e.UserEmail is { } id && amendedNames.TryGetValue(id, out var n) ? n : e.UserEmail ?? "(unknown)",
                e.WorkDate,
                e.IssueKey,
                Math.Round(e.TimeSpentSeconds / 3600.0, 2),
                Math.Round((e.AmendedSourceSeconds ?? e.TimeSpentSeconds) / 3600.0, 2)))
            .ToList();

        return new AutoSubmitReportData(
            LocalRunTime: localRunTime,
            Submitted: submitted,
            DuplicateCount: runSubmissions.Count(s => s.Status == SubmissionStatus.Duplicate),
            FailedCount: failures.Count,
            FirstError: failures.FirstOrDefault()?.ErrorMessage,
            ConflictCount: await db.ImportedEntries.CountAsync(e => e.Status == ImportStatus.Conflict, ct),
            PendingUnmappedCount: await db.ImportedEntries.CountAsync(e => e.Status == ImportStatus.Pending, ct),
            NeedsTaskCount: await db.ImportedEntries.CountAsync(
                e => e.Status == ImportStatus.Mapped && e.TimelogTaskId == null, ct),
            NewEntriesSinceLastRun: newEntriesSinceLastRun,
            NewlyAmended: newlyAmended,
            Import: import,
            StepFailures: stepFailures);
    }

    private async Task MarkAmendmentsReportedAsync(
        IReadOnlyList<AmendedAfterSubmission> amendments,
        CancellationToken ct)
    {
        var ids = amendments.Select(a => a.EntryId).ToList();
        var rows = await db.ImportedEntries.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var row in rows)
            row.AmendmentReportedAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Marked {Count} amendment(s) as reported", rows.Count);
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Time zone '{TimeZone}' not found — falling back to UTC", options.Value.TimeZone);
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>
/// Thrown when one of the run's steps failed. Carries the failures purely so a caller can
/// see them; the job has already reported and recorded them by the time this is thrown.
/// </summary>
public class AutoSubmitStepFailedException(IReadOnlyList<StepFailure> failures)
    : Exception($"AutoSubmitReportJob: {failures.Count} step(s) failed — "
                + string.Join(" | ", failures.Select(f => $"{f.Step}: {f.Error}")))
{
    public IReadOnlyList<StepFailure> Failures { get; } = failures;
}
