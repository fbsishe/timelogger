using Microsoft.EntityFrameworkCore;
using TimeLogger.Application.Mapping;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Services;

public class MappingRuleService(AppDbContext db, IMappingEngine engine) : IMappingRuleService
{
    public async Task<IReadOnlyList<MappingRuleDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.MappingRules
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .OrderBy(r => r.Priority)
            .Select(r => new MappingRuleDto(
                r.Id, r.Name, r.SourceType, r.MatchField, r.MatchOperator, r.MatchValue,
                r.TimelogProjectId, r.TimelogProject.Name,
                r.TimelogTaskId, r.TimelogTask != null ? r.TimelogTask.Name : null,
                r.Priority, r.IsEnabled))
            .ToListAsync(ct);

    public async Task<MappingRule> CreateAsync(CreateMappingRuleRequest req, CancellationToken ct = default)
    {
        var rule = new MappingRule
        {
            Name = req.Name,
            SourceType = req.SourceType,
            MatchField = req.MatchField,
            MatchOperator = req.MatchOperator,
            MatchValue = req.MatchValue,
            TimelogProjectId = req.TimelogProjectId,
            TimelogTaskId = req.TimelogTaskId,
            Priority = req.Priority,
            IsEnabled = true,
        };
        db.MappingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task UpdateAsync(int id, CreateMappingRuleRequest req, CancellationToken ct = default)
    {
        var rule = await db.MappingRules.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Rule {id} not found.");
        rule.Name = req.Name;
        rule.SourceType = req.SourceType;
        rule.MatchField = req.MatchField;
        rule.MatchOperator = req.MatchOperator;
        rule.MatchValue = req.MatchValue;
        rule.TimelogProjectId = req.TimelogProjectId;
        rule.TimelogTaskId = req.TimelogTaskId;
        rule.Priority = req.Priority;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.MappingRules.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Rule {id} not found.");
        db.MappingRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct = default)
    {
        var rule = await db.MappingRules.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Rule {id} not found.");
        rule.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task MovePriorityAsync(int id, int direction, CancellationToken ct = default)
    {
        var rules = await db.MappingRules.OrderBy(r => r.Priority).ToListAsync(ct);
        var idx = rules.FindIndex(r => r.Id == id);
        if (idx < 0) return;

        var swapIdx = idx + direction;
        if (swapIdx < 0 || swapIdx >= rules.Count) return;

        (rules[idx].Priority, rules[swapIdx].Priority) = (rules[swapIdx].Priority, rules[idx].Priority);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EntryListItem>> TestRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.MappingRules.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Rule {id} not found.");

        var entries = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending || e.Status == ImportStatus.Failed)
            .Include(e => e.ImportSource)
            .ToListAsync(ct);

        return entries
            .Where(e => engine.Matches(rule, e))
            .Select(e => new EntryListItem(
                e.Id, e.ExternalId,
                e.ImportSource != null ? e.ImportSource.Name : "Unknown",
                e.WorkDate, Math.Round(e.TimeSpentSeconds / 3600.0, 2),
                e.ProjectKey, e.IssueKey, e.Description, e.UserEmail, e.Status.ToString()))
            .ToList();
    }
}
