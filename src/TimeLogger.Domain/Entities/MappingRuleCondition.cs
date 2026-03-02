namespace TimeLogger.Domain.Entities;

public class MappingRuleCondition
{
    public int Id { get; set; }
    public int MappingRuleId { get; set; }
    public MappingRule MappingRule { get; set; } = null!;

    /// <summary>
    /// Field on <see cref="ImportedEntry"/> (or a key inside MetadataJson) to inspect.
    /// Use dot-notation for metadata fields, e.g. "metadata.customfield_10200".
    /// </summary>
    public required string MatchField { get; set; }

    public MatchOperator MatchOperator { get; set; }
    public required string MatchValue { get; set; }
}
