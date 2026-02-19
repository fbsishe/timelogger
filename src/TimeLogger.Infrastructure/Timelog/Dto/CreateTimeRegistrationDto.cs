using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class CreateTimeRegistrationDto
{
    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("Date")]
    public required string Date { get; set; }

    /// <summary>Decimal hours, e.g. 1.5 for 1h30m.</summary>
    [JsonPropertyName("Hours")]
    public double Hours { get; set; }

    [JsonPropertyName("Comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("Billable")]
    public bool Billable { get; set; }
}
