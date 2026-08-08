namespace TimeLogger.Infrastructure.Timelog;

/// <summary>One time registration ("work unit") as reported by the Timelog Reporting API.</summary>
public record WorkUnitItem(
    string? TimeRegistrationGuid,
    int UserId,
    int TaskId,
    string? TaskName,
    string? ProjectName,
    DateOnly Date,
    string? Note,
    double Hours,
    int ApprovedStatus,
    bool Invoiced,
    DateTime? Created,
    DateTime? LastModified);

public interface ITimelogReportingClient
{
    /// <summary>False when Reporting API credentials are not configured.</summary>
    bool IsConfigured { get; }

    /// <summary>All employees' time registrations with a work date in the given range.</summary>
    Task<IReadOnlyList<WorkUnitItem>> GetWorkUnitsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
