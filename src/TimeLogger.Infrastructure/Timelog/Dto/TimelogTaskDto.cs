using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimelogTaskDto
{
    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("ID")]
    public string? Id { get; set; }

    [JsonPropertyName("TaskName")]
    public required string Name { get; set; }

    [JsonPropertyName("TaskNo")]
    public string? No { get; set; }

    [JsonPropertyName("ProjectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("IsActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("ParentFullName")]
    public string? ParentFullName { get; set; }
}
