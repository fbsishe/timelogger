namespace TimeLogger.Application.Services;

public record AuditLogItem(
    int Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Category,
    string Action,
    string? Subject,
    string? Details);

public interface IAuditLogService
{
    /// <summary>Records a configuration change; the actor is resolved from the current user.</summary>
    Task LogAsync(string category, string action, string? subject = null, string? details = null, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogItem>> GetRecentAsync(int limit = 500, CancellationToken ct = default);
}

/// <summary>
/// Resolves the identity of the user performing the current operation.
/// Returns null outside an authenticated context (e.g. background jobs).
/// </summary>
public interface ICurrentUserProvider
{
    Task<string?> GetCurrentUserAsync(CancellationToken ct = default);
}
