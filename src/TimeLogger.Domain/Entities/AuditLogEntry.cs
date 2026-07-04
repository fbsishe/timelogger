namespace TimeLogger.Domain.Entities;

/// <summary>A configuration change made by a user, recorded for the admin audit trail.</summary>
public class AuditLogEntry
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Who made the change — user email/name, or "system" for background processes.</summary>
    public required string Actor { get; set; }

    /// <summary>What kind of object changed, e.g. "MappingRule", "ImportSource".</summary>
    public required string Category { get; set; }

    /// <summary>What happened, e.g. "Created", "Updated", "Deleted", "Enabled".</summary>
    public required string Action { get; set; }

    /// <summary>Which object changed, e.g. the rule name.</summary>
    public string? Subject { get; set; }

    /// <summary>Optional free-form detail about the change.</summary>
    public string? Details { get; set; }
}
