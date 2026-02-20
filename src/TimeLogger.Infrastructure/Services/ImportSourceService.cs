using Hangfire;
using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Tempo;

namespace TimeLogger.Infrastructure.Services;

public class ImportSourceService(AppDbContext db, IBackgroundJobClient jobs) : IImportSourceService
{
    public async Task<IReadOnlyList<ImportSourceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var sources = await db.ImportSources.ToListAsync(ct);
        var result = new List<ImportSourceDto>();

        foreach (var s in sources)
        {
            var total = await db.ImportedEntries.CountAsync(e => e.ImportSourceId == s.Id, ct);
            var pending = await db.ImportedEntries.CountAsync(
                e => e.ImportSourceId == s.Id && e.Status == ImportStatus.Pending, ct);
            result.Add(new ImportSourceDto(s.Id, s.Name, s.SourceType, s.BaseUrl,
                s.PollSchedule, s.IsEnabled, s.LastPolledAt, total, pending));
        }
        return result;
    }

    public async Task<ImportSource> CreateAsync(string name, SourceType sourceType,
        string? baseUrl, string? apiToken, string? schedule, CancellationToken ct = default)
    {
        var source = new ImportSource
        {
            Name = name, SourceType = sourceType, BaseUrl = baseUrl,
            ApiToken = apiToken, PollSchedule = schedule, IsEnabled = true,
        };
        db.ImportSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source;
    }

    public async Task UpdateAsync(int id, string name, string? baseUrl, string? apiToken,
        string? schedule, bool isEnabled, CancellationToken ct = default)
    {
        var source = await db.ImportSources.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Source {id} not found.");
        source.Name = name;
        source.BaseUrl = baseUrl;
        if (!string.IsNullOrWhiteSpace(apiToken)) source.ApiToken = apiToken;
        source.PollSchedule = schedule;
        source.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var source = await db.ImportSources.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Source {id} not found.");
        db.ImportSources.Remove(source);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ImportHistoryEntry>> GetImportHistoryAsync(
        int days = 30, CancellationToken ct = default)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

        return await db.ImportedEntries
            .Where(e => e.WorkDate >= since)
            .Include(e => e.ImportSource)
            .GroupBy(e => new { e.WorkDate, SourceName = e.ImportSource != null ? e.ImportSource.Name : "Unknown" })
            .Select(g => new ImportHistoryEntry(
                g.Key.WorkDate,
                g.Key.SourceName,
                g.Count(),
                g.Count(e => e.Status == ImportStatus.Pending),
                g.Count(e => e.Status == ImportStatus.Mapped),
                g.Count(e => e.Status == ImportStatus.Submitted),
                g.Count(e => e.Status == ImportStatus.Failed)))
            .OrderByDescending(e => e.WorkDate)
            .ToListAsync(ct);
    }

    public Task TriggerPollAllAsync(CancellationToken ct = default)
    {
        jobs.Enqueue<PullTempoWorklogsJob>(j => j.ExecuteAsync(CancellationToken.None));
        return Task.CompletedTask;
    }
}
