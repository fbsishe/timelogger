using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimeTrackingItemDto
{
    [JsonPropertyName("TimeRegistrationID")]
    public int TimeRegistrationId { get; set; }

    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("Hours")]
    public double Hours { get; set; }

    [JsonPropertyName("Date")]
    public string? Date { get; set; }

    [JsonPropertyName("Comment")]
    public string? Comment { get; set; }
}
