using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Mapping;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Mapping;

public class MappingSuggestionOptions
{
    public const string SectionName = "Mapping";

    /// <summary>
    /// Metadata key of the Jira "Timelog Account" custom field, e.g. "customfield_10501".
    /// When empty, all customfield_* metadata keys are scanned for values that
    /// match a Timelog project name.
    /// </summary>
    public string? TimelogAccountFieldKey { get; set; }
}

public class MappingSuggestionService(
    IMappingEngine engine,
    AppDbContext db,
    IOptions<MappingSuggestionOptions> options,
    ILogger<MappingSuggestionService> logger) : IMappingSuggestionService
{
    public async Task<IReadOnlyList<MappingSuggestionDto>> GetSuggestionsAsync(CancellationToken ct = default)
    {
        var pending = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending)
            .Include(e => e.ImportSource)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return [];

        var rules = await db.MappingRules
            .Where(r => r.IsEnabled)
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .Include(r => r.Conditions)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        var unmatched = pending
            .Where(e => !engine.Evaluate(rules, e).IsMatched)
            .ToList();

        if (unmatched.Count == 0)
            return [];

        var projects = await db.TimelogProjects
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        var projectsByName = projects
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var configuredKey = options.Value.TimelogAccountFieldKey;

        // Group unmatched entries by (metadata key, account value)
        var groups = new Dictionary<(string Key, string Value), (int Count, string? ProjectKey)>();

        foreach (var entry in unmatched)
        {
            foreach (var (key, value) in ExtractAccountCandidates(entry.MetadataJson, configuredKey))
            {
                var groupKey = (key, value);
                groups[groupKey] = groups.TryGetValue(groupKey, out var existing)
                    ? (existing.Count + 1, existing.ProjectKey ?? entry.ProjectKey)
                    : (1, entry.ProjectKey);
            }
        }

        var suggestions = new List<MappingSuggestionDto>();

        foreach (var ((fieldKey, accountValue), (count, projectKey)) in groups)
        {
            if (!projectsByName.TryGetValue(Normalize(accountValue), out var project))
                continue;

            suggestions.Add(new MappingSuggestionDto(
                MetadataFieldKey: fieldKey,
                AccountValue: accountValue,
                PendingEntryCount: count,
                SampleProjectKey: projectKey,
                TimelogProjectId: project.Id,
                TimelogProjectName: project.Name,
                IsExactMatch: string.Equals(accountValue.Trim(), project.Name.Trim(), StringComparison.Ordinal)));
        }

        logger.LogInformation(
            "Found {SuggestionCount} mapping suggestions across {UnmatchedCount} unmatched pending entries",
            suggestions.Count, unmatched.Count);

        return suggestions
            .OrderByDescending(s => s.PendingEntryCount)
            .ThenBy(s => s.AccountValue)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Extracts candidate "Timelog Account" values from an entry's metadata.
    /// With a configured field key only that key is read; otherwise every
    /// customfield_* key with a non-empty string value is considered.
    /// </summary>
    private static IEnumerable<(string Key, string Value)> ExtractAccountCandidates(
        string? metadataJson, string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(configuredKey))
                {
                    if (!string.Equals(prop.Name, configuredKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else if (!prop.Name.StartsWith("customfield_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = ExtractStringValue(prop.Value);
                if (!string.IsNullOrWhiteSpace(value))
                    yield return (prop.Name, value);
            }
        }
    }

    /// <summary>
    /// Jira custom fields can be plain strings or option objects like
    /// {"value": "..."} / {"name": "..."} — serialized to string in metadata.
    /// </summary>
    private static string? ExtractStringValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // The import stores complex custom-field values as their JSON text;
            // unwrap common option-object shapes.
            if (raw.TrimStart().StartsWith('{'))
            {
                try
                {
                    using var inner = JsonDocument.Parse(raw);
                    if (inner.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString();
                    if (inner.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        return n.GetString();
                }
                catch (JsonException)
                {
                    // Not JSON after all — use the raw string
                }
            }

            return raw;
        }

        return null;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
}
