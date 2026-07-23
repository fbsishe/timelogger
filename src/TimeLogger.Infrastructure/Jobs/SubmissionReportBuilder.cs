using System.Text;

namespace TimeLogger.Infrastructure.Jobs;

/// <summary>Hours one employee had submitted into one Timelog project during a run.</summary>
public record SubmittedGroup(string Employee, string Project, int EntryCount, double Hours);

/// <summary>Everything the Slack report needs, collected after an auto-submit run.</summary>
public record AutoSubmitReportData(
    DateTimeOffset LocalRunTime,
    IReadOnlyList<SubmittedGroup> Submitted,
    int DuplicateCount,
    int FailedCount,
    string? FirstError,
    int ConflictCount,
    int PendingUnmappedCount,
    int NeedsTaskCount,
    int NewEntriesSinceLastRun);

/// <summary>Renders the auto-submit run report as Slack mrkdwn.</summary>
public static class SubmissionReportBuilder
{
    public static string Build(AutoSubmitReportData data)
    {
        var sb = new StringBuilder();
        sb.Append(":stopwatch: *TimeLogger auto-submit — ")
          .Append(data.LocalRunTime.ToString("ddd dd MMM, HH:mm"))
          .AppendLine("*");

        if (data.Submitted.Count > 0)
        {
            var totalHours = data.Submitted.Sum(g => g.Hours);
            var totalEntries = data.Submitted.Sum(g => g.EntryCount);
            sb.AppendLine($"*Submitted {FormatHours(totalHours)} across {totalEntries} entr{(totalEntries == 1 ? "y" : "ies")}:*");
            foreach (var g in data.Submitted.OrderBy(g => g.Employee).ThenBy(g => g.Project))
                sb.AppendLine($"• {g.Employee} → {g.Project}: {FormatHours(g.Hours)} ({g.EntryCount})");
        }
        else
        {
            sb.AppendLine("No entries were submitted this run.");
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

        if (attention.Count > 0)
        {
            sb.AppendLine("*Needs attention:*");
            foreach (var line in attention)
                sb.AppendLine($"• {line}");
        }
        else if (data.Submitted.Count == 0 && data.DuplicateCount == 0)
        {
            sb.AppendLine(":white_check_mark: All clear — nothing needs handling.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatHours(double hours) => $"{hours:0.##}h";
    private static string Plural(int count) => count == 1 ? "" : "s";
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
