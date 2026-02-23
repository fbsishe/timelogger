namespace TimeLogger.Domain.Entities;

public class EmployeeMapping
{
    public int Id { get; set; }
    public required string AtlassianAccountId { get; set; }
    public string? DisplayName { get; set; }
    public int TimelogUserId { get; set; }
    public string? TimelogUserDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
