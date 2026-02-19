namespace TimeLogger.Domain.Entities;

public class SubmittedEntry
{
    public int Id { get; set; }

    public int ImportedEntryId { get; set; }
    public ImportedEntry ImportedEntry { get; set; } = null!;

    /// <summary>Hour registration ID returned by the Timelog API.</summary>
    public string? ExternalId { get; set; }

    public SubmissionStatus Status { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Error message if submission failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Number of submission attempts (for retry tracking).</summary>
    public int AttemptCount { get; set; } = 1;
}
