namespace TimeLogger.Domain.Entities;

public class ImportSource
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public SourceType SourceType { get; set; }

    /// <summary>Bearer token or API key for API-based sources.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Base URL for API-based sources (e.g. Tempo API base URL).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Cron expression controlling when this source is polled (e.g. "0 6 * * *").</summary>
    public string? PollSchedule { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPolledAt { get; set; }

    public ICollection<ImportedEntry> ImportedEntries { get; set; } = [];
}
