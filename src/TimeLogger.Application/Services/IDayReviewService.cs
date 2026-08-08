using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;

namespace TimeLogger.Application.Services;

public enum DayGroupState
{
    /// <summary>Hours match on both sides (within 0.01).</summary>
    Match,
    /// <summary>Both sides have hours on this task but they differ.</summary>
    Different,
    /// <summary>We have entries for this task but Timelog has no registration.</summary>
    MissingInTimelog,
    /// <summary>Timelog has a registration but we have no entry for this task (e.g. manually entered).</summary>
    OnlyInTimelog,
    /// <summary>Our entries are not mapped to a Timelog task, so they cannot be compared.</summary>
    Unmapped,
}

/// <summary>One ImportedEntry on the Timelogger side of the comparison.</summary>
public record DayReviewEntry(
    int Id,
    string? IssueKey,
    string? Description,
    double Hours,
    string Status);

/// <summary>One time registration on the Timelog side of the comparison.</summary>
public record DayReviewRegistration(
    int TimeRegistrationId,
    string? TaskName,
    string? ProjectName,
    double Hours,
    string? Comment,
    int? ApprovalStatus,
    bool? Invoiced,
    DateTime? Created,
    DateTime? LastModified)
{
    /// <summary>
    /// Best-effort "already approved/closed in Timelog" signal. The approval enum is
    /// undocumented; values 6 and 7 are what approved months show in practice.
    /// </summary>
    public bool IsApproved => ApprovalStatus >= 6;
}

/// <summary>All entries and registrations for one Timelog task on one person's day.</summary>
public record DayReviewGroup(
    int? ApiTaskId,
    string TaskName,
    IReadOnlyList<DayReviewEntry> Entries,
    IReadOnlyList<DayReviewRegistration> Registrations,
    DayGroupState State)
{
    public double OurHours => Math.Round(Entries.Sum(e => e.Hours), 2);
    public double TimelogHours => Math.Round(Registrations.Sum(r => r.Hours), 2);

    /// <summary>
    /// A difference is one-click resolvable only in the 1:1 case — the same shape the
    /// submission-time conflict check handles.
    /// </summary>
    public bool CanQuickResolve => State == DayGroupState.Different
        && Entries.Count == 1 && Registrations.Count == 1;
}

public record DayReviewResult(
    string UserDisplay,
    DateOnly Date,
    bool TimelogQueried,
    string? Warning,
    IReadOnlyList<DayReviewGroup> Groups,
    string? TimesheetStatus = null)
{
    public double OurTotal => Math.Round(Groups.Sum(g => g.OurHours), 2);
    public double TimelogTotal => Math.Round(Groups.Sum(g => g.TimelogHours), 2);

    /// <summary>The person's Timelog week is submitted/approved — writes will be rejected (422 "Date is closed by Timesheet").</summary>
    public bool IsTimesheetClosed => string.Equals(TimesheetStatus, "Closed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Side-by-side comparison of one person's day: our imported entries vs the
/// registrations actually present in Timelog.
/// </summary>
public interface IDayReviewService
{
    /// <summary>
    /// Builds the comparison for the given Atlassian account and work date.
    /// Ignored entries are excluded. When Timelog cannot be queried (no employee mapping,
    /// API error) the result carries a warning and only our side is populated.
    /// </summary>
    Task<DayReviewResult> GetDayReviewAsync(string accountId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Resolves a 1:1 hour difference against a live Timelog registration, regardless of the
    /// entry's current status (covers both unresolved conflicts and post-submission drift).
    /// Records the conflict on the entry, then reuses the standard resolution flow.
    /// </summary>
    Task<SubmitOutcome> ResolveDifferenceAsync(
        int entryId,
        int timelogRegistrationId,
        double timelogHours,
        ConflictResolution resolution,
        double? customHours = null,
        CancellationToken ct = default);
}
