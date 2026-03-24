using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Mapping;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Mapping;

public class ApplyMappingsService(
    IMappingEngine engine,
    AppDbContext db,
    ILogger<ApplyMappingsService> logger) : IApplyMappingsService
{
    public async Task<int> ApplyAllPendingAsync(CancellationToken cancellationToken = default)
    {
        var entries = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending)
            .Include(e => e.ImportSource)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
            return 0;

        // Auto-skip entries for excluded employees
        var excludedAccountIds = await db.EmployeeMappings
            .Where(m => m.IsExcluded)
            .Select(m => m.AtlassianAccountId)
            .ToListAsync(cancellationToken);

        int excluded = 0;
        if (excludedAccountIds.Count > 0)
        {
            foreach (var entry in entries.Where(e => e.UserEmail != null && excludedAccountIds.Contains(e.UserEmail!)))
            {
                entry.Status = ImportStatus.Ignored;
                excluded++;
            }
            if (excluded > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Auto-skipped {Count} entries for excluded employees", excluded);
            }
        }

        var pending = entries.Where(e => e.Status == ImportStatus.Pending).ToList();

        var rules = await LoadRulesAsync(cancellationToken);
        if (rules.Count == 0 || pending.Count == 0)
        {
            logger.LogInformation("No enabled mapping rules or no pending entries — skipping rule application");
            return excluded;
        }

        logger.LogInformation("Applying mapping rules to {Count} pending entries", pending.Count);

        int mapped = 0;

        foreach (var entry in pending)
        {
            var result = engine.Evaluate(rules, entry);

            if (!result.IsMatched)
                continue;

            entry.Status = ImportStatus.Mapped;
            entry.MappingRuleId = result.MatchedRule!.Id;
            entry.TimelogProjectId = result.Project!.Id;
            entry.TimelogTaskId = result.Task?.Id;
            mapped++;
        }

        if (mapped > 0)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Mapped {Mapped}/{Total} pending entries", mapped, pending.Count);
        return excluded + mapped;
    }

    public async Task<IReadOnlyList<ImportedEntry>> TestRuleAsync(
        int ruleId,
        CancellationToken cancellationToken = default)
    {
        var rule = await db.MappingRules
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken)
            ?? throw new InvalidOperationException($"MappingRule {ruleId} not found.");

        var entries = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending)
            .Include(e => e.ImportSource)
            .ToListAsync(cancellationToken);

        return entries
            .Where(e => engine.Matches(rule, e))
            .ToList()
            .AsReadOnly();
    }

    public async Task<int> ApplyRuleAsync(int ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await db.MappingRules
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken)
            ?? throw new InvalidOperationException($"MappingRule {ruleId} not found.");

        var entries = await db.ImportedEntries
            .Where(e => e.Status == ImportStatus.Pending)
            .Include(e => e.ImportSource)
            .ToListAsync(cancellationToken);

        int mapped = 0;
        foreach (var entry in entries.Where(e => engine.Matches(rule, e)))
        {
            entry.Status = ImportStatus.Mapped;
            entry.MappingRuleId = rule.Id;
            entry.TimelogProjectId = rule.TimelogProject.Id;
            entry.TimelogTaskId = rule.TimelogTask?.Id;
            mapped++;
        }

        if (mapped > 0)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Rule {RuleId} mapped {Count} entries", ruleId, mapped);
        return mapped;
    }

    private async Task<List<MappingRule>> LoadRulesAsync(CancellationToken cancellationToken) =>
        await db.MappingRules
            .Where(r => r.IsEnabled)
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .Include(r => r.Conditions)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
}
