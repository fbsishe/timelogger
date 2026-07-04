using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Services;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Services;

/// <summary>Fallback for contexts without a signed-in user (background jobs, tests).</summary>
public class SystemCurrentUserProvider : ICurrentUserProvider
{
    public Task<string?> GetCurrentUserAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

public class AuditLogService(
    AppDbContext db,
    ICurrentUserProvider currentUserProvider) : IAuditLogService
{
    public async Task LogAsync(
        string category, string action, string? subject = null, string? details = null,
        CancellationToken ct = default)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = await currentUserProvider.GetCurrentUserAsync(ct) ?? "system",
            Category = category,
            Action = action,
            Subject = subject,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogItem>> GetRecentAsync(int limit = 500, CancellationToken ct = default)
    {
        var entries = await db.AuditLogEntries
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(ct);

        return entries
            .Select(e => new AuditLogItem(e.Id, e.Timestamp, e.Actor, e.Category, e.Action, e.Subject, e.Details))
            .ToList()
            .AsReadOnly();
    }
}
