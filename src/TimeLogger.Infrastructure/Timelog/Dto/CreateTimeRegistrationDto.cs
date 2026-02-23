using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class CreateTimeRegistrationDto
{
    [JsonPropertyName("ID")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("GroupType")]
    public int GroupType { get; set; } = 1; // 1 = Project, 3 = Absence

    [JsonPropertyName("Date")]
    public required string Date { get; set; }

    /// <summary>Decimal hours, e.g. 1.5 for 1h30m.</summary>
    [JsonPropertyName("Hours")]
    public double Hours { get; set; }

    [JsonPropertyName("Comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("Billable")]
    public bool Billable { get; set; }

    [JsonPropertyName("UserID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UserId { get; set; }
}
