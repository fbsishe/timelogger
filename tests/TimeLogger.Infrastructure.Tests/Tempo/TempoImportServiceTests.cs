using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Jira;
using TimeLogger.Infrastructure.Jira.Dto;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Tempo;
using TimeLogger.Infrastructure.Tempo.Dto;

namespace TimeLogger.Infrastructure.Tests.Tempo;

public class TempoImportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IJiraApiClient> _jiraMock;
    private readonly TempoImportService _sut;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private ImportSource _source = null!;

    public TempoImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _jiraMock = new Mock<IJiraApiClient>();
        _httpHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        var factory = CreateHttpClientFactory(_httpHandlerMock);
        var tempoOptions = Options.Create(new TempoOptions { BaseUrl = "https://api.tempo.io/4" });

        _sut = new TempoImportService(
            factory,
            _jiraMock.Object,
            _db,
            tempoOptions,
            NullLogger<TempoImportService>.Instance);
    }

    private async Task<ImportSource> SeedSourceAsync()
    {
        var source = new ImportSource
        {
            Name = "Tempo Test",
            SourceType = SourceType.Tempo,
            ApiToken = "test-token",
            IsEnabled = true,
        };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }

    private static IHttpClientFactory CreateHttpClientFactory(Mock<HttpMessageHandler> handlerMock)
    {
        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("Tempo")).Returns(client);
        return factoryMock.Object;
    }

    private void SetupTempoResponse(IEnumerable<TempoWorklogDto> worklogs)
    {
        var payload = new TempoPagedResponse<TempoWorklogDto>
        {
            Results = worklogs.ToList(),
            Metadata = new TempoMetadata { Count = worklogs.Count(), Offset = 0, Limit = 5000 },
        };

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload)),
            });
    }

    private static TempoWorklogDto MakeWorklog(long id, long issueId = 100, int seconds = 3600) =>
        new()
        {
            TempoWorklogId = id,
            TimeSpentSeconds = seconds,
            BillableSeconds = seconds,
            StartDate = "2024-03-15",
            Description = $"Work on issue {issueId}",
            Author = new TempoAuthor { AccountId = "user-account-123" },
            Issue = new TempoIssueRef { Id = issueId },
        };

    private void SetupJiraIssue(long issueId, string key, string projectKey,
        Dictionary<string, object?>? extras = null)
    {
        var fields = new JiraIssueFields
        {
            Summary = "Test issue",
            Project = new JiraProject { Key = projectKey },
        };

        if (extras is not null)
        {
            // Build ExtensionData from extras dict using JsonElement
            var json = JsonSerializer.Serialize(extras);
            var doc = JsonDocument.Parse(json);
            fields.ExtensionData = doc.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        _jiraMock
            .Setup(c => c.GetIssueAsync(issueId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraIssueDto { Id = issueId.ToString(), Key = key, Fields = fields });
    }

    [Fact]
    public async Task ImportAsync_PersistsNewWorklogsAsImportedEntries()
    {
        _source = await SeedSourceAsync();
        SetupTempoResponse([MakeWorklog(1, issueId: 100), MakeWorklog(2, issueId: 101)]);
        SetupJiraIssue(100, "PROJ-1", "PROJ");
        SetupJiraIssue(101, "PROJ-2", "PROJ");

        var count = await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        Assert.Equal(2, count);
        var entries = await _db.ImportedEntries.ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(ImportStatus.Pending, e.Status);
            Assert.Equal("PROJ", e.ProjectKey);
            Assert.Equal("user-account-123", e.UserEmail);
        });
    }

    [Fact]
    public async Task ImportAsync_SkipsDuplicatesByExternalId()
    {
        _source = await SeedSourceAsync();

        // Pre-seed one entry
        _db.ImportedEntries.Add(new ImportedEntry
        {
            ImportSourceId = _source.Id,
            ExternalId = "1",
            UserEmail = "user-account-123",
            WorkDate = new DateOnly(2024, 3, 15),
            TimeSpentSeconds = 3600,
            Status = ImportStatus.Pending,
        });
        await _db.SaveChangesAsync();

        SetupTempoResponse([MakeWorklog(1), MakeWorklog(2)]);
        SetupJiraIssue(100, "PROJ-1", "PROJ");

        var count = await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        Assert.Equal(1, count); // Only worklog 2 is new
        Assert.Equal(2, await _db.ImportedEntries.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_StoresIssueKeyAndProjectKey()
    {
        _source = await SeedSourceAsync();
        SetupTempoResponse([MakeWorklog(1, issueId: 42)]);
        SetupJiraIssue(42, "ALPHA-7", "ALPHA");

        await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal("ALPHA-7", entry.IssueKey);
        Assert.Equal("ALPHA", entry.ProjectKey);
    }

    [Fact]
    public async Task ImportAsync_StoresCustomFieldsInMetadataJson()
    {
        _source = await SeedSourceAsync();
        SetupTempoResponse([MakeWorklog(1, issueId: 10)]);
        SetupJiraIssue(10, "T-1", "T", new Dictionary<string, object?>
        {
            ["customfield_10200"] = "timelog-task-99",
            ["customfield_10300"] = null,
        });

        await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.NotNull(entry.MetadataJson);

        var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entry.MetadataJson!);
        Assert.NotNull(meta);
        Assert.True(meta.ContainsKey("customfield_10200"));
    }

    [Fact]
    public async Task ImportAsync_ContinuesWhenJiraCallFails()
    {
        _source = await SeedSourceAsync();
        SetupTempoResponse([MakeWorklog(1, issueId: 999)]);

        _jiraMock
            .Setup(c => c.GetIssueAsync(999, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Jira unreachable"));

        // Should still import the entry, just without Jira enrichment
        var count = await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        Assert.Equal(1, count);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Null(entry.ProjectKey);
        Assert.Null(entry.IssueKey);
    }

    [Fact]
    public async Task ImportAsync_CorrectlyConvertsTimeSpentSeconds()
    {
        _source = await SeedSourceAsync();
        SetupTempoResponse([MakeWorklog(1, issueId: 1, seconds: 5400)]); // 1h30m
        SetupJiraIssue(1, "X-1", "X");

        await _sut.ImportAsync(_source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15));

        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(5400, entry.TimeSpentSeconds);
    }

    [Fact]
    public async Task ImportAsync_ThrowsWhenSourceNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ImportAsync(999, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15)));
    }

    [Fact]
    public async Task ImportAsync_ThrowsWhenSourceHasNoToken()
    {
        var source = new ImportSource
        {
            Name = "No token",
            SourceType = SourceType.Tempo,
            ApiToken = null,
        };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ImportAsync(source.Id, new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 15)));
    }

    [Fact]
    public async Task ImportYesterdayAsync_OnlyImportsFromEnabledSources()
    {
        var enabled = new ImportSource
        {
            Name = "Enabled", SourceType = SourceType.Tempo, ApiToken = "tok", IsEnabled = true,
        };
        var disabled = new ImportSource
        {
            Name = "Disabled", SourceType = SourceType.Tempo, ApiToken = "tok", IsEnabled = false,
        };
        _db.ImportSources.AddRange(enabled, disabled);
        await _db.SaveChangesAsync();

        SetupTempoResponse([]);

        await _sut.ImportYesterdayAsync();

        // Disabled source should never trigger a Tempo HTTP call for its entries
        // (enabled source makes one call returning 0 results)
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
