using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimelogTimeRegistrationDto
{
    [JsonPropertyName("ID")]
    public string? Id { get; set; }

    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("Date")]
    public string? Date { get; set; }

    [JsonPropertyName("Hours")]
    public double Hours { get; set; }

    [JsonPropertyName("UserID")]
    public int? UserId { get; set; }
}
