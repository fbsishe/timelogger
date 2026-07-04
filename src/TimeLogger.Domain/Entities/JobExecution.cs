namespace TimeLogger.Domain.Entities;

/// <summary>One execution of a background job, recorded for health monitoring.</summary>
public class JobExecution
{
    public int Id { get; set; }
    public required string JobName { get; set; }
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
