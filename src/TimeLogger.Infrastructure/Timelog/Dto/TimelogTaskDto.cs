using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimelogTaskDto
{
    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("ID")]
    public required string Id { get; set; }

    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("No")]
    public string? No { get; set; }

    [JsonPropertyName("ProjectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("ParentTaskID")]
    public int? ParentTaskId { get; set; }

    [JsonPropertyName("ParentFullName")]
    public string? ParentFullName { get; set; }
}
