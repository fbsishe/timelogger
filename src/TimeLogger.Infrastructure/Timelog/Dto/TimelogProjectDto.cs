using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimelogProjectDto
{
    [JsonPropertyName("ProjectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("ID")]
    public required string Id { get; set; }

    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("No")]
    public string? No { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("CustomerID")]
    public int CustomerId { get; set; }
}
