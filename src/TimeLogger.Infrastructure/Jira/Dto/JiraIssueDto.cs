using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Jira.Dto;

public class JiraIssueDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("fields")]
    public JiraIssueFields? Fields { get; set; }
}

public class JiraIssueFields
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("project")]
    public JiraProject? Project { get; set; }

    /// <summary>
    /// Captures all remaining fields, including custom fields (customfield_XXXXX).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class JiraProject
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
