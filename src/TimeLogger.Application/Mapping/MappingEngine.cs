using System.Text.Json;
using System.Text.RegularExpressions;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Mapping;

public sealed class MappingEngine : IMappingEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MappingResult Evaluate(IEnumerable<MappingRule> rules, ImportedEntry entry)
    {
        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            // Filter by source system if the rule is scoped
            if (rule.SourceType.HasValue)
            {
                var sourceType = entry.ImportSource?.SourceType ?? DeriveSourceType(entry);
                if (sourceType != rule.SourceType.Value)
                    continue;
            }

            if (Matches(rule, entry))
                return MappingResult.Matched(rule, rule.TimelogProject, rule.TimelogTask);
        }

        return MappingResult.Unmatched();
    }

    public bool Matches(MappingRule rule, ImportedEntry entry)
    {
        var fieldValue = ResolveField(rule.MatchField, entry);
        if (fieldValue is null)
            return false;

        return rule.MatchOperator switch
        {
            Domain.MatchOperator.Equals =>
                string.Equals(fieldValue, rule.MatchValue, StringComparison.OrdinalIgnoreCase),

            Domain.MatchOperator.Contains =>
                fieldValue.Contains(rule.MatchValue, StringComparison.OrdinalIgnoreCase),

            Domain.MatchOperator.StartsWith =>
                fieldValue.StartsWith(rule.MatchValue, StringComparison.OrdinalIgnoreCase),

            Domain.MatchOperator.Regex =>
                Regex.IsMatch(fieldValue, rule.MatchValue,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),

            _ => false,
        };
    }

    // ------------------------------------------------------------------
    // Field resolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves the value of a named field from an <see cref="ImportedEntry"/>.
    /// Supports:
    ///   - Standard fields: ProjectKey, IssueKey, UserEmail, Description, Activity
    ///   - Metadata fields: "metadata.{key}" — looks up <paramref name="entry"/>.MetadataJson
    /// Field names are case-insensitive.
    /// </summary>
    internal static string? ResolveField(string fieldName, ImportedEntry entry)
    {
        if (fieldName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
        {
            var key = fieldName["metadata.".Length..];
            return ResolveMetadataField(key, entry.MetadataJson);
        }

        return fieldName.ToLowerInvariant() switch
        {
            "projectkey"  => entry.ProjectKey,
            "issuekey"    => entry.IssueKey,
            "useremail"   => entry.UserEmail,
            "description" => entry.Description,
            "activity"    => entry.Activity,
            _ => null,
        };
    }

    private static string? ResolveMetadataField(string key, string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            // Try exact key first, then case-insensitive search
            if (doc.RootElement.TryGetProperty(key, out var exact))
                return JsonElementToString(exact);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    return JsonElementToString(prop.Value);
            }
        }
        catch (JsonException)
        {
            // Malformed metadata — treat as no value
        }

        return null;
    }

    private static string? JsonElementToString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null   => null,
            JsonValueKind.Undefined => null,
            _                    => element.GetRawText(),
        };

    private static Domain.SourceType DeriveSourceType(ImportedEntry entry) =>
        entry.ImportSource?.SourceType ?? Domain.SourceType.Tempo;
}
