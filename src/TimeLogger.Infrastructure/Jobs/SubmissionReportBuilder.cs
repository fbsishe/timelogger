using System.Text;

namespace TimeLogger.Infrastructure.Jobs;

/// <summary>Hours one employee had submitted into one Timelog project on the report day.</summary>
public record SubmittedGroup(string Employee, string Project, int EntryCount, double Hours);

/// <summary>
/// A worklog the source amended after we had already pushed it to Timelog. Our row and the
/// Timelog registration now disagree with the source, and only a human can decide which wins.
/// </summary>
public record AmendedAfterSubmission(
    int EntryId,
    string Employee,
    DateOnly WorkDate,
    string? IssueKey,
    double SubmittedHours,
    double SourceHours);

/// <summary>
/// A step of the auto-submit run that threw. The run carries on with the remaining steps,
/// so the report has to say which part of it did not happen.
/// </summary>
public record StepFailure(string Step, string Error);

/// <summary>Everything the morning digest needs: what reached Timelog on <see cref="ReportDay"/>.</summary>
public record AutoSubmitReportData(
    DateTimeOffset LocalRunTime,
    DateOnly ReportDay,
    IReadOnlyList<SubmittedGroup> Submitted,
    int DuplicateCount,
    int FailedCount,
    string? FirstError,
    int ConflictCount,
    int PendingUnmappedCount,
    int NeedsTaskCount,
    IReadOnlyList<AmendedAfterSubmission> NewlyAmended,
    IReadOnlyList<StepFailure>? StepFailures = null);

/// <summary>Renders the auto-submit Slack messages as mrkdwn.</summary>
public static class SubmissionReportBuilder
{
    /// <summary>
    /// The morning digest: everything submitted on the report day, plus whatever still
    /// needs a human. The only message the job posts when nothing went wrong.
    /// </summary>
    public static string Build(AutoSubmitReportData data)
    {
        var sb = new StringBuilder();
        var day = data.ReportDay.ToString("ddd dd MMM");
        sb.Append(":stopwatch: *TimeLogger auto-submit — daily report for ")
          .Append(day)
          .AppendLine("*");

        var stepFailures = data.StepFailures ?? [];
        AppendStepFailures(sb, stepFailures);

        if (data.Submitted.Count > 0)
        {
            var totalHours = data.Submitted.Sum(g => g.Hours);
            var totalEntries = data.Submitted.Sum(g => g.EntryCount);
            sb.AppendLine($"*Submitted {FormatHours(totalHours)} across {totalEntries} entr{(totalEntries == 1 ? "y" : "ies")} on {day}:*");
            foreach (var g in data.Submitted.OrderBy(g => g.Employee).ThenBy(g => g.Project))
                sb.AppendLine($"• {g.Employee} → {g.Project}: {FormatHours(g.Hours)} ({g.EntryCount})");
        }
        else
        {
            sb.AppendLine($"No entries were submitted on {day}.");
        }

        if (data.DuplicateCount > 0)
            sb.AppendLine($"_{data.DuplicateCount} entr{(data.DuplicateCount == 1 ? "y was" : "ies were")} already in Timelog (skipped)._");

        var attention = new List<string>();
        if (data.ConflictCount > 0)
            attention.Add($":warning: {data.ConflictCount} conflicting registration{Plural(data.ConflictCount)} — resolve on the Submission page");
        if (data.FailedCount > 0)
            attention.Add($":x: {data.FailedCount} failed submission{Plural(data.FailedCount)}" +
                          (data.FirstError is null ? "" : $" — first error: {Truncate(data.FirstError, 140)}"));
        if (data.PendingUnmappedCount > 0)
            attention.Add($":grey_question: {data.PendingUnmappedCount} unmapped entr{(data.PendingUnmappedCount == 1 ? "y" : "ies")} waiting for a mapping rule");
        if (data.NeedsTaskCount > 0)
            attention.Add($":pushpin: {data.NeedsTaskCount} mapped entr{(data.NeedsTaskCount == 1 ? "y" : "ies")} missing a Timelog task");
        if (data.NewlyAmended.Count > 0)
        {
            attention.Add(
                $":pencil2: {data.NewlyAmended.Count} worklog{Plural(data.NewlyAmended.Count)} " +
                $"amended in the source *after* we submitted {(data.NewlyAmended.Count == 1 ? "it" : "them")} " +
                "to Timelog — Timelog still holds the old hours:");

            foreach (var a in data.NewlyAmended
                         .OrderBy(a => a.Employee)
                         .ThenBy(a => a.WorkDate)
                         .Take(MaxAmendmentsListed))
            {
                var delta = a.SourceHours - a.SubmittedHours;
                var sign = delta > 0 ? "+" : "";
                attention.Add(
                    $"    ◦ {a.Employee}, {a.WorkDate:yyyy-MM-dd}" +
                    (a.IssueKey is null ? "" : $" ({a.IssueKey})") +
                    $": submitted {FormatHours(a.SubmittedHours)}, source now says " +
                    $"{FormatHours(a.SourceHours)} ({sign}{FormatHours(delta)})");
            }

            if (data.NewlyAmended.Count > MaxAmendmentsListed)
                attention.Add($"    ◦ …and {data.NewlyAmended.Count - MaxAmendmentsListed} more — see the Entries page");
        }

        if (attention.Count > 0)
        {
            sb.AppendLine("*Needs attention:*");
            foreach (var line in attention)
                sb.AppendLine($"• {line}");
        }
        else if (stepFailures.Count == 0)
        {
            sb.AppendLine(":white_check_mark: All clear — nothing needs handling.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// What the daytime runs post instead of a report — only ever sent when a step threw
    /// or a submission was rejected. Successful submissions wait for the morning digest.
    /// </summary>
    public static string BuildErrorAlert(
        DateTimeOffset localRunTime,
        IReadOnlyList<StepFailure> stepFailures,
        int failedCount,
        string? firstError)
    {
        var sb = new StringBuilder();
        sb.Append(":rotating_light: *TimeLogger auto-submit — errors in the ")
          .Append(localRunTime.ToString("HH:mm"))
          .Append(" run, ")
          .Append(localRunTime.ToString("ddd dd MMM"))
          .AppendLine("*");

        AppendStepFailures(sb, stepFailures);

        if (failedCount > 0)
            sb.AppendLine($"• :x: {failedCount} failed submission{Plural(failedCount)}" +
                          (firstError is null ? "" : $" — first error: {Truncate(firstError, 140)}"));

        return sb.ToString().TrimEnd();
    }

    private static void AppendStepFailures(StringBuilder sb, IReadOnlyList<StepFailure> stepFailures)
    {
        if (stepFailures.Count == 0) return;

        sb.AppendLine($":rotating_light: *{stepFailures.Count} step{Plural(stepFailures.Count)} failed — this run is incomplete:*");
        foreach (var failure in stepFailures)
            sb.AppendLine($"• {failure.Step}: {Truncate(failure.Error, 200)}");
    }

    /// <summary>Keeps a bad day from posting a wall of Slack text.</summary>
    private const int MaxAmendmentsListed = 10;

    private static string FormatHours(double hours) => $"{hours:0.##}h";
    private static string Plural(int count) => count == 1 ? "" : "s";
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
