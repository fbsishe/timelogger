using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimelogUserDto
{
    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("FirstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("LastName")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
