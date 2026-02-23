using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Jira.Dto;

public class JiraUserDto
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }
}
