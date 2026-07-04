namespace TimeLogger.Application.Services;

/// <summary>
/// A suggested project mapping for pending entries that no enabled rule matches,
/// derived by matching the Jira "Timelog Account" custom field value against
/// Timelog project names.
/// </summary>
public record MappingSuggestionDto(
    string MetadataFieldKey,
    string AccountValue,
    int PendingEntryCount,
    string? SampleProjectKey,
    int TimelogProjectId,
    string TimelogProjectName,
    bool IsExactMatch);

public interface IMappingSuggestionService
{
    /// <summary>
    /// Scans pending entries not matched by any enabled rule and suggests
    /// Timelog projects whose name matches the entries' "Timelog Account" field value.
    /// </summary>
    Task<IReadOnlyList<MappingSuggestionDto>> GetSuggestionsAsync(CancellationToken ct = default);
}
