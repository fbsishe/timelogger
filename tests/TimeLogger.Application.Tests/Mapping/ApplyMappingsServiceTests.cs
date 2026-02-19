using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Mapping;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Mapping;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Application.Tests.Mapping;

public class ApplyMappingsServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IMappingEngine> _engineMock;
    private readonly ApplyMappingsService _sut;

    public ApplyMappingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _engineMock = new Mock<IMappingEngine>();
        _sut = new ApplyMappingsService(_engineMock.Object, _db, NullLogger<ApplyMappingsService>.Instance);
    }

    private async Task<(ImportSource source, TimelogProject project, TimelogTask task)> SeedBaseDataAsync()
    {
        var source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(source);

        var project = new TimelogProject
        {
            ExternalId = "proj-1", Name = "Alpha", LastSyncedAt = DateTimeOffset.UtcNow,
        };
        _db.TimelogProjects.Add(project);

        var task = new TimelogTask
        {
            ExternalId = "task-1", Name = "Dev", TimelogProject = project, LastSyncedAt = DateTimeOffset.UtcNow,
        };
        _db.TimelogTasks.Add(task);

        await _db.SaveChangesAsync();
        return (source, project, task);
    }

    private async Task<MappingRule> SeedRuleAsync(int projectId, int? taskId = null)
    {
        var rule = new MappingRule
        {
            Name = "Rule", MatchField = "ProjectKey",
            MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
            Priority = 1, TimelogProjectId = projectId, TimelogTaskId = taskId,
            IsEnabled = true,
        };
        _db.MappingRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    private async Task<ImportedEntry> SeedEntryAsync(int sourceId, ImportStatus status = ImportStatus.Pending)
    {
        var entry = new ImportedEntry
        {
            ImportSourceId = sourceId, ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "dev@example.com", WorkDate = new DateOnly(2024, 1, 1),
            TimeSpentSeconds = 3600, Status = status,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task ApplyAllPendingAsync_MapsMatchedEntries()
    {
        var (source, project, task) = await SeedBaseDataAsync();
        var rule = await SeedRuleAsync(project.Id, task.Id);
        var entry = await SeedEntryAsync(source.Id);

        _engineMock
            .Setup(e => e.Evaluate(It.IsAny<IEnumerable<MappingRule>>(), entry))
            .Returns(MappingResult.Matched(rule, project, task));

        var count = await _sut.ApplyAllPendingAsync();

        Assert.Equal(1, count);

        var updated = await _db.ImportedEntries.FindAsync(entry.Id);
        Assert.Equal(ImportStatus.Mapped, updated!.Status);
        Assert.Equal(project.Id, updated.TimelogProjectId);
        Assert.Equal(task.Id, updated.TimelogTaskId);
        Assert.Equal(rule.Id, updated.MappingRuleId);
    }

    [Fact]
    public async Task ApplyAllPendingAsync_LeavesUnmatchedEntriesPending()
    {
        var (source, _, _) = await SeedBaseDataAsync();
        await SeedRuleAsync(1);
        var entry = await SeedEntryAsync(source.Id);

        _engineMock
            .Setup(e => e.Evaluate(It.IsAny<IEnumerable<MappingRule>>(), entry))
            .Returns(MappingResult.Unmatched());

        var count = await _sut.ApplyAllPendingAsync();

        Assert.Equal(0, count);

        var updated = await _db.ImportedEntries.FindAsync(entry.Id);
        Assert.Equal(ImportStatus.Pending, updated!.Status);
    }

    [Fact]
    public async Task ApplyAllPendingAsync_IgnoresAlreadyMappedOrSubmittedEntries()
    {
        var (source, _, _) = await SeedBaseDataAsync();
        await SeedRuleAsync(1);
        await SeedEntryAsync(source.Id, ImportStatus.Mapped);
        await SeedEntryAsync(source.Id, ImportStatus.Submitted);

        var count = await _sut.ApplyAllPendingAsync();

        Assert.Equal(0, count);
        // Engine should never be called for non-Pending entries
        _engineMock.Verify(
            e => e.Evaluate(It.IsAny<IEnumerable<MappingRule>>(), It.IsAny<ImportedEntry>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyAllPendingAsync_ReturnsZeroWhenNoRulesExist()
    {
        var (source, _, _) = await SeedBaseDataAsync();
        // No rules seeded
        await SeedEntryAsync(source.Id);

        var count = await _sut.ApplyAllPendingAsync();

        Assert.Equal(0, count);
        _engineMock.Verify(
            e => e.Evaluate(It.IsAny<IEnumerable<MappingRule>>(), It.IsAny<ImportedEntry>()),
            Times.Never);
    }

    [Fact]
    public async Task TestRuleAsync_ReturnsEntriesMatchedByRule()
    {
        var (source, project, task) = await SeedBaseDataAsync();
        var rule = await SeedRuleAsync(project.Id, task.Id);
        // Re-load rule with navigation properties (EF in-memory doesn't auto-load navs)
        var loadedRule = await _db.MappingRules
            .Include(r => r.TimelogProject)
            .Include(r => r.TimelogTask)
            .FirstAsync(r => r.Id == rule.Id);

        var matchEntry = await SeedEntryAsync(source.Id);
        var noMatchEntry = await SeedEntryAsync(source.Id);

        _engineMock
            .Setup(e => e.Matches(It.Is<MappingRule>(r => r.Id == loadedRule.Id), matchEntry))
            .Returns(true);
        _engineMock
            .Setup(e => e.Matches(It.Is<MappingRule>(r => r.Id == loadedRule.Id), noMatchEntry))
            .Returns(false);

        var results = await _sut.TestRuleAsync(rule.Id);

        Assert.Single(results);
        Assert.Equal(matchEntry.Id, results[0].Id);
    }

    [Fact]
    public async Task TestRuleAsync_ThrowsWhenRuleNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TestRuleAsync(9999));
    }

    [Fact]
    public async Task TestRuleAsync_DoesNotPersistAnyChanges()
    {
        var (source, project, task) = await SeedBaseDataAsync();
        var rule = await SeedRuleAsync(project.Id, task.Id);
        var entry = await SeedEntryAsync(source.Id);

        _engineMock
            .Setup(e => e.Matches(It.IsAny<MappingRule>(), entry))
            .Returns(true);

        await _sut.TestRuleAsync(rule.Id);

        // Entry status must remain Pending — TestRule is read-only
        var unchanged = await _db.ImportedEntries.FindAsync(entry.Id);
        Assert.Equal(ImportStatus.Pending, unchanged!.Status);
    }

    public void Dispose() => _db.Dispose();
}
