using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

public class TimeTrackingItemDto
{
    [JsonPropertyName("TimeRegistrationID")]
    public int TimeRegistrationId { get; set; }

    [JsonPropertyName("TaskID")]
    public int TaskId { get; set; }

    [JsonPropertyName("TaskName")]
    public string? TaskName { get; set; }

    [JsonPropertyName("ProjectID")]
    public int? ProjectId { get; set; }

    [JsonPropertyName("ProjectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("Hours")]
    public double Hours { get; set; }

    [JsonPropertyName("Date")]
    public string? Date { get; set; }

    [JsonPropertyName("Comment")]
    public string? Comment { get; set; }

    /// <summary>
    /// Timelog's approval-workflow status for the registration. Undocumented enum;
    /// observed values in this tenant: 6 and 7 on approved months, so treat >= 6 as approved.
    /// </summary>
    [JsonPropertyName("TimeRegistrationApprovalStatus")]
    public int? ApprovalStatus { get; set; }

    [JsonPropertyName("InvoiceStatus")]
    public bool? InvoiceStatus { get; set; }

    [JsonPropertyName("Created")]
    public DateTime? Created { get; set; }

    [JsonPropertyName("LastModified")]
    public DateTime? LastModified { get; set; }
}
