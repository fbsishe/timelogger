namespace TimeLogger.Domain.Entities;

public class ImportedEntry
{
    public int Id { get; set; }

    public int ImportSourceId { get; set; }
    public ImportSource ImportSource { get; set; } = null!;

    /// <summary>Unique identifier from the source system (worklog ID, row hash, etc.).</summary>
    public required string ExternalId { get; set; }

    public required string UserEmail { get; set; }
    public DateOnly WorkDate { get; set; }

    /// <summary>Time spent in seconds.</summary>
    public int TimeSpentSeconds { get; set; }

    public string? Description { get; set; }

    // Source-system fields used by the mapping engine
    public string? ProjectKey { get; set; }
    public string? IssueKey { get; set; }
    public string? Activity { get; set; }

    /// <summary>
    /// JSON bag of additional fields from the source system
    /// (e.g. Jira custom fields, CSV columns). Used by mapping rules.
    /// </summary>
    public string? MetadataJson { get; set; }

    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? MappingRuleId { get; set; }
    public MappingRule? MappingRule { get; set; }

    public int? TimelogProjectId { get; set; }
    public TimelogProject? TimelogProject { get; set; }

    public int? TimelogTaskId { get; set; }
    public TimelogTask? TimelogTask { get; set; }

    public SubmittedEntry? SubmittedEntry { get; set; }
}
