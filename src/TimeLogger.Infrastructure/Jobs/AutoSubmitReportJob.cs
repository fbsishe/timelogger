using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job (production only, AutoSubmit:Enabled) — submits all
/// non-problematic mapped entries to Timelog, then posts a run report to Slack.
/// Conflicting or unmapped entries are never auto-pushed; they are surfaced in
/// the report's "needs attention" section instead.
/// </summary>
public class AutoSubmitReportJob(
    AppDbContext db,
    ITimelogSubmissionService submissionService,
    IJobHealthService jobHealth,
    ISlackMessageSender slack,
    IOptions<AutoSubmitOptions> options,
    ILogger<AutoSubmitReportJob> logger)
{
    public const string JobId = "timelog-auto-submit";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var runStartUtc = DateTimeOffset.UtcNow;
        logger.LogInformation("AutoSubmitReportJob started at {Time}", runStartUtc);

        try
        {
            var lastRunUtc = await db.JobExecutions
                .Where(e => e.JobName == JobId)
                .OrderByDescending(e => e.ExecutedAt)
                .Select(e => (DateTimeOffset?)e.ExecutedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var newEntriesSinceLastRun = await db.ImportedEntries
                .CountAsync(e => e.ImportedAt > (lastRunUtc ?? DateTimeOffset.MinValue), cancellationToken);

            await submissionService.SubmitAllPendingAsync(cancellationToken);

            var localNow = TimeZoneInfo.ConvertTime(runStartUtc, ResolveTimeZone());
            var data = await CollectReportDataAsync(runStartUtc, localNow, newEntriesSinceLastRun, cancellationToken);

            var anythingNew = newEntriesSinceLastRun > 0
                || data.Submitted.Count > 0
                || data.DuplicateCount > 0
                || data.FailedCount > 0;

            if (ShouldSendReport(localNow, anythingNew))
            {
                var sent = await slack.SendAsync(SubmissionReportBuilder.Build(data), cancellationToken);
                logger.LogInformation("AutoSubmitReportJob report {Outcome}", sent ? "sent to Slack" : "NOT sent");
            }
            else
            {
                logger.LogInformation("AutoSubmitReportJob: nothing new — report suppressed for this run");
            }

            await jobHealth.RecordSuccessAsync(JobId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoSubmitReportJob failed");
            await jobHealth.RecordFailureAsync(JobId, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// The 08:00 weekday run is an always-on heartbeat; every other run
    /// (13:00, 17:00, weekends) only reports when something new happened.
    /// </summary>
    public static bool ShouldSendReport(DateTimeOffset localNow, bool anythingNew)
    {
        var isWeekday = localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        return (isWeekday && localNow.Hour == 8) || anythingNew;
    }

    private async Task<AutoSubmitReportData> CollectReportDataAsync(
        DateTimeOffset runStartUtc,
        DateTimeOffset localRunTime,
        int newEntriesSinceLastRun,
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
            NewEntriesSinceLastRun: newEntriesSinceLastRun);
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
