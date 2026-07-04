using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Mapping;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Mapping;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Application.Tests.Mapping;

public class MappingSuggestionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public MappingSuggestionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    private MappingSuggestionService CreateSut(string? accountFieldKey = null) =>
        new(
            new MappingEngine(),
            _db,
            Options.Create(new MappingSuggestionOptions { TimelogAccountFieldKey = accountFieldKey }),
            NullLogger<MappingSuggestionService>.Instance);

    private async Task<ImportSource> SeedSourceAsync()
    {
        var source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }

    private async Task<TimelogProject> SeedProjectAsync(string name, bool isActive = true)
    {
        var project = new TimelogProject
        {
            ExternalId = Guid.NewGuid().ToString(),
            Name = name,
            IsActive = isActive,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        _db.TimelogProjects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<ImportedEntry> SeedEntryAsync(
        int sourceId,
        string? metadataJson,
        ImportStatus status = ImportStatus.Pending,
        string? projectKey = "APHJ")
    {
        var entry = new ImportedEntry
        {
            ImportSourceId = sourceId,
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "dev@example.com",
            WorkDate = new DateOnly(2026, 7, 1),
            TimeSpentSeconds = 3600,
            Status = status,
            ProjectKey = projectKey,
            MetadataJson = metadataJson,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task GetSuggestionsAsync_SuggestsExactNameMatch_FromConfiguredField()
    {
        var source = await SeedSourceAsync();
        var project = await SeedProjectAsync("APHJ / 5983 Service Requests (IT Foundation)");
        await SeedEntryAsync(source.Id,
            """{"customfield_10501":"APHJ / 5983 Service Requests (IT Foundation)"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        var s = Assert.Single(suggestions);
        Assert.Equal("customfield_10501", s.MetadataFieldKey);
        Assert.Equal(project.Id, s.TimelogProjectId);
        Assert.True(s.IsExactMatch);
        Assert.Equal(1, s.PendingEntryCount);
        Assert.Equal("APHJ", s.SampleProjectKey);
    }

    [Fact]
    public async Task GetSuggestionsAsync_UnwrapsOptionObjectValues()
    {
        var source = await SeedSourceAsync();
        var project = await SeedProjectAsync("Alpha Project");
        await SeedEntryAsync(source.Id,
            """{"customfield_10501":"{\"self\":\"https://x\",\"value\":\"Alpha Project\",\"id\":\"1\"}"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        var s = Assert.Single(suggestions);
        Assert.Equal(project.Id, s.TimelogProjectId);
        Assert.Equal("Alpha Project", s.AccountValue);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ScansAllCustomFields_WhenNoKeyConfigured()
    {
        var source = await SeedSourceAsync();
        var project = await SeedProjectAsync("Beta Project");
        await SeedEntryAsync(source.Id,
            """{"customfield_10001":"unrelated","customfield_10002":"Beta Project"}""");

        var suggestions = await CreateSut().GetSuggestionsAsync();

        var s = Assert.Single(suggestions);
        Assert.Equal("customfield_10002", s.MetadataFieldKey);
        Assert.Equal(project.Id, s.TimelogProjectId);
    }

    [Fact]
    public async Task GetSuggestionsAsync_IgnoresEntriesMatchedByEnabledRule()
    {
        var source = await SeedSourceAsync();
        var project = await SeedProjectAsync("Gamma Project");
        _db.MappingRules.Add(new MappingRule
        {
            Name = "Existing",
            Conditions = [new MappingRuleCondition
            {
                MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals,
                MatchValue = "APHJ",
            }],
            TimelogProjectId = project.Id,
            Priority = 1,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        await SeedEntryAsync(source.Id, """{"customfield_10501":"Gamma Project"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ReturnsEmpty_WhenNoProjectNameMatches()
    {
        var source = await SeedSourceAsync();
        await SeedProjectAsync("Completely Different");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"No Such Project"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NormalizedMatch_IsNotExact()
    {
        var source = await SeedSourceAsync();
        var project = await SeedProjectAsync("Delta  Project");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"delta project"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        var s = Assert.Single(suggestions);
        Assert.Equal(project.Id, s.TimelogProjectId);
        Assert.False(s.IsExactMatch);
    }

    [Fact]
    public async Task GetSuggestionsAsync_GroupsEntriesByAccountValue()
    {
        var source = await SeedSourceAsync();
        await SeedProjectAsync("Epsilon Project");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"Epsilon Project"}""");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"Epsilon Project"}""");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"Epsilon Project"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        var s = Assert.Single(suggestions);
        Assert.Equal(3, s.PendingEntryCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_IgnoresInactiveProjects()
    {
        var source = await SeedSourceAsync();
        await SeedProjectAsync("Zeta Project", isActive: false);
        await SeedEntryAsync(source.Id, """{"customfield_10501":"Zeta Project"}""");

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_IgnoresNonPendingEntries()
    {
        var source = await SeedSourceAsync();
        await SeedProjectAsync("Eta Project");
        await SeedEntryAsync(source.Id, """{"customfield_10501":"Eta Project"}""",
            status: ImportStatus.Mapped);

        var suggestions = await CreateSut("customfield_10501").GetSuggestionsAsync();

        Assert.Empty(suggestions);
    }

    public void Dispose() => _db.Dispose();
}
