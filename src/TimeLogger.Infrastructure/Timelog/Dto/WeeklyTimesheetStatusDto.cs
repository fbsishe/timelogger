using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class WeeklyTimesheetStatusDto
{
    [JsonPropertyName("EmployeeUserID")]
    public int EmployeeUserId { get; set; }

    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("Details")]
    public List<WeeklyTimesheetStatusDetailDto> Details { get; set; } = [];
}

public class WeeklyTimesheetStatusDetailDto
{
    [JsonPropertyName("WeekNumber")]
    public int WeekNumber { get; set; }

    /// <summary>Observed values: "Open", "Closed" (submitted for approval / approved).</summary>
    [JsonPropertyName("TimesheetStatus")]
    public string? TimesheetStatus { get; set; }

    [JsonPropertyName("StartDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("EndDate")]
    public string? EndDate { get; set; }
}
