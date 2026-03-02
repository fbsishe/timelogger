namespace TimeLogger.Domain.Entities;

public class MappingRule
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Null means the rule applies to all source systems.</summary>
    public SourceType? SourceType { get; set; }

    public ICollection<MappingRuleCondition> Conditions { get; set; } = [];

    public int TimelogProjectId { get; set; }
    public TimelogProject TimelogProject { get; set; } = null!;

    /// <summary>Optional — when null, only the project is matched.</summary>
    public int? TimelogTaskId { get; set; }
    public TimelogTask? TimelogTask { get; set; }

    /// <summary>Lower numbers are evaluated first.</summary>
    public int Priority { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ImportedEntry> MatchedEntries { get; set; } = [];
}
