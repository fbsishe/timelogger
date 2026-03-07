namespace TimeLogger.Domain.Entities;

public class TimelogTask
{
    public int Id { get; set; }

    /// <summary>GUID ID as returned by the Timelog API (used for deduplication).</summary>
    public required string ExternalId { get; set; }

    /// <summary>Integer TaskID as returned by the Timelog API (required when posting time registrations).</summary>
    public int? ApiTaskId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSyncedAt { get; set; }

    public int TimelogProjectId { get; set; }
    public TimelogProject TimelogProject { get; set; } = null!;

    public ICollection<MappingRule> MappingRules { get; set; } = [];
    public ICollection<ImportedEntry> ImportedEntries { get; set; } = [];
}
