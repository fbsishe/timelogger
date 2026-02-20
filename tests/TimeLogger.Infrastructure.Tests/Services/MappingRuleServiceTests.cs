using Microsoft.EntityFrameworkCore;
using Moq;
using TimeLogger.Application.Mapping;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Services;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Tests.Services;

public class MappingRuleServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IMappingEngine> _engineMock;
    private readonly MappingRuleService _sut;

    public MappingRuleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _engineMock = new Mock<IMappingEngine>();
        _sut = new MappingRuleService(_db, _engineMock.Object);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task<MappingRule> SeedRuleAsync(AppDbContext db)
    {
        var project = new TimelogProject
        {
            ExternalId = "proj-1",
            Name = "Test Project",
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.TimelogProjects.Add(project);
        await db.SaveChangesAsync();

        var rule = new MappingRule
        {
            Name = "Test Rule",
            MatchField = "ProjectKey",
            MatchOperator = MatchOperator.Equals,
            MatchValue = "PROJ",
            Priority = 1,
            TimelogProjectId = project.Id,
            IsEnabled = true,
        };
        db.MappingRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    private static async Task<ImportedEntry> SeedEntryAsync(
        AppDbContext db, int sourceId, ImportStatus status = ImportStatus.Pending)
    {
        var entry = new ImportedEntry
        {
            ImportSourceId = sourceId,
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "dev@example.com",
            WorkDate = new DateOnly(2024, 1, 1),
            TimeSpentSeconds = 3600,
            Status = status,
        };
        db.ImportedEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private async Task<ImportSource> SeedSourceAsync()
    {
        var source = new ImportSource
        {
            Name = "Test Source",
            SourceType = SourceType.FileUpload,
            IsEnabled = true,
        };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }

    // ------------------------------------------------------------------
    // TestRuleAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task TestRuleAsync_ReturnsMatchingPendingEntries()
    {
        var source = await SeedSourceAsync();
        var rule = await SeedRuleAsync(_db);
        var matchEntry = await SeedEntryAsync(_db, source.Id, ImportStatus.Pending);
        var noMatchEntry = await SeedEntryAsync(_db, source.Id, ImportStatus.Pending);

        _engineMock
            .Setup(e => e.Matches(It.Is<MappingRule>(r => r.Id == rule.Id), matchEntry))
            .Returns(true);
        _engineMock
            .Setup(e => e.Matches(It.Is<MappingRule>(r => r.Id == rule.Id), noMatchEntry))
            .Returns(false);

        var results = await _sut.TestRuleAsync(rule.Id);

        Assert.Single(results);
        Assert.Equal(matchEntry.Id, results[0].Id);
    }

    [Fact]
    public async Task TestRuleAsync_DoesNotReturnNonPendingEntries()
    {
        var source = await SeedSourceAsync();
        var rule = await SeedRuleAsync(_db);

        var mappedEntry = await SeedEntryAsync(_db, source.Id, ImportStatus.Mapped);
        var submittedEntry = await SeedEntryAsync(_db, source.Id, ImportStatus.Submitted);

        // Engine would match all entries if asked — but they should never be queried
        _engineMock
            .Setup(e => e.Matches(It.IsAny<MappingRule>(), It.IsAny<ImportedEntry>()))
            .Returns(true);

        var results = await _sut.TestRuleAsync(rule.Id);

        Assert.Empty(results);
    }

    [Fact]
    public async Task TestRuleAsync_ReturnsFailedEntries()
    {
        var source = await SeedSourceAsync();
        var rule = await SeedRuleAsync(_db);
        var failedEntry = await SeedEntryAsync(_db, source.Id, ImportStatus.Failed);

        _engineMock
            .Setup(e => e.Matches(It.IsAny<MappingRule>(), failedEntry))
            .Returns(true);

        var results = await _sut.TestRuleAsync(rule.Id);

        Assert.Single(results);
        Assert.Equal(failedEntry.Id, results[0].Id);
    }

    [Fact]
    public async Task TestRuleAsync_ThrowsWhenRuleNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TestRuleAsync(9999));
    }

    [Fact]
    public async Task TestRuleAsync_ReturnsEmptyWhenNoEntriesMatch()
    {
        var source = await SeedSourceAsync();
        var rule = await SeedRuleAsync(_db);
        await SeedEntryAsync(_db, source.Id, ImportStatus.Pending);

        _engineMock
            .Setup(e => e.Matches(It.IsAny<MappingRule>(), It.IsAny<ImportedEntry>()))
            .Returns(false);

        var results = await _sut.TestRuleAsync(rule.Id);

        Assert.Empty(results);
    }

    public void Dispose() => _db.Dispose();
}
