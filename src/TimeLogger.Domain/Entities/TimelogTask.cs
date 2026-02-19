namespace TimeLogger.Domain.Entities;

public class TimelogTask
{
    public int Id { get; set; }

    /// <summary>ID as returned by the Timelog API.</summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSyncedAt { get; set; }

    public int TimelogProjectId { get; set; }
    public TimelogProject TimelogProject { get; set; } = null!;

    public ICollection<MappingRule> MappingRules { get; set; } = [];
    public ICollection<ImportedEntry> ImportedEntries { get; set; } = [];
}
