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
    private readonly List<string> _requestUrls = [];
    private readonly TempoOptions _tempoOptions = new()
    {
        BaseUrl = "https://api.tempo.io/4",
        LookbackDays = 90,
        WatermarkOverlapMinutes = 120,
    };
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

        _sut = new TempoImportService(
            factory,
            _jiraMock.Object,
            _db,
            Options.Create(_tempoOptions),
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
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                _requestUrls.Add(req.RequestUri!.ToString()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload)),
            });
    }

    private void SetupTempoFailure()
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Tempo unreachable"));
    }

    private static TempoWorklogDto MakeWorklog(
        long id,
        long issueId = 100,
        int seconds = 3600,
        string startDate = "2024-03-15",
        string? description = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? createdAt = null) =>
        new()
        {
            TempoWorklogId = id,
            TimeSpentSeconds = seconds,
            BillableSeconds = seconds,
            StartDate = startDate,
            Description = description ?? $"Work on issue {issueId}",
            Author = new TempoAuthor { AccountId = "user-account-123" },
            Issue = new TempoIssueRef { Id = issueId },
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
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
    public async Task ImportIncrementalAsync_OnlyImportsFromEnabledSources()
    {
        var enabled = new ImportSource
        {
            Name = "Enabled",
            SourceType = SourceType.Tempo,
            ApiToken = "tok",
            IsEnabled = true,
        };
        var disabled = new ImportSource
        {
            Name = "Disabled",
            SourceType = SourceType.Tempo,
            ApiToken = "tok",
            IsEnabled = false,
        };
        _db.ImportSources.AddRange(enabled, disabled);
        await _db.SaveChangesAsync();

        SetupTempoResponse([]);

        await _sut.ImportIncrementalAsync();

        // Disabled source should never trigger a Tempo HTTP call for its entries
        // (enabled source makes one call returning 0 results)
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Incremental pull (TL-111) — back-dated worklogs and amendments
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportIncrementalAsync_SendsUpdatedFromDerivedFromWatermark()
    {
        var source = await SeedSourceAsync();
        source.LastPolledAt = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        await _db.SaveChangesAsync();

        SetupTempoResponse([]);

        await _sut.ImportIncrementalAsync();

        var url = Assert.Single(_requestUrls);
        // 12:00 minus the 120-minute overlap
        Assert.Contains("updatedFrom=2026-09-03T10:00:00Z", url);
    }

    [Fact]
    public async Task ImportIncrementalAsync_OmitsUpdatedFromOnFirstRun()
    {
        var source = await SeedSourceAsync();
        Assert.Null(source.LastPolledAt);

        SetupTempoResponse([]);

        await _sut.ImportIncrementalAsync();

        var url = Assert.Single(_requestUrls);
        Assert.DoesNotContain("updatedFrom", url);
        Assert.Contains("from=", url);
    }

    [Fact]
    public async Task ImportIncrementalAsync_BoundsWorkDatesByLookbackDays()
    {
        _tempoOptions.LookbackDays = 30;
        await SeedSourceAsync();
        SetupTempoResponse([]);

        await _sut.ImportIncrementalAsync();

        var expectedFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        Assert.Contains($"from={expectedFrom:yyyy-MM-dd}", Assert.Single(_requestUrls));
    }

    /// <summary>
    /// The regression this whole change exists for: a worklog reported today for a work date
    /// weeks ago must still be imported. The old "yesterday only" pull could never see it.
    /// </summary>
    [Fact]
    public async Task ImportIncrementalAsync_ImportsBackDatedWorklog()
    {
        var source = await SeedSourceAsync();
        source.LastPolledAt = DateTimeOffset.UtcNow.AddHours(-6);
        await _db.SaveChangesAsync();

        var workDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        SetupTempoResponse([MakeWorklog(
            77,
            issueId: 500,
            startDate: workDate.ToString("yyyy-MM-dd"),
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow)]);
        SetupJiraIssue(500, "OLD-1", "OLD");

        var result = await _sut.ImportIncrementalAsync();

        Assert.Equal(1, result.Imported);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal("77", entry.ExternalId);
        Assert.Equal(workDate, entry.WorkDate);
    }

    [Fact]
    public async Task ImportIncrementalAsync_RefreshesAmendedEntryAndResetsForRemapping()
    {
        var source = await SeedSourceAsync();
        source.LastPolledAt = DateTimeOffset.UtcNow.AddHours(-6);
        _db.ImportedEntries.Add(new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = "5",
            UserEmail = "user-account-123",
            WorkDate = new DateOnly(2024, 3, 15),
            TimeSpentSeconds = 3600,
            Description = "Work on issue 100",
            Status = ImportStatus.Mapped,
            TimelogTaskId = null,
            SourceUpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await _db.SaveChangesAsync();

        SetupTempoResponse([MakeWorklog(5, seconds: 18000, updatedAt: DateTimeOffset.UtcNow)]);
        SetupJiraIssue(100, "PROJ-1", "PROJ");

        var result = await _sut.ImportIncrementalAsync();

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Refreshed);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(18000, entry.TimeSpentSeconds);
        Assert.Equal(ImportStatus.Pending, entry.Status);
    }

    [Fact]
    public async Task ImportIncrementalAsync_LeavesAlreadySubmittedEntryUntouched()
    {
        var source = await SeedSourceAsync();
        source.LastPolledAt = DateTimeOffset.UtcNow.AddHours(-6);
        _db.ImportedEntries.Add(new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = "6",
            UserEmail = "user-account-123",
            WorkDate = new DateOnly(2024, 3, 15),
            TimeSpentSeconds = 3600,
            Description = "Work on issue 100",
            Status = ImportStatus.Submitted,
            SourceUpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await _db.SaveChangesAsync();

        SetupTempoResponse([MakeWorklog(6, seconds: 7200, updatedAt: DateTimeOffset.UtcNow)]);
        SetupJiraIssue(100, "PROJ-1", "PROJ");

        var result = await _sut.ImportIncrementalAsync();

        Assert.Equal(1, result.ChangedAfterSubmission);
        Assert.Equal(0, result.Refreshed);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(3600, entry.TimeSpentSeconds);            // not rewritten
        Assert.Equal(ImportStatus.Submitted, entry.Status);     // not re-queued
    }

    [Fact]
    public async Task ImportIncrementalAsync_SkipsWorklogWhoseUpdatedAtHasNotMoved()
    {
        var source = await SeedSourceAsync();
        source.LastPolledAt = DateTimeOffset.UtcNow.AddHours(-6);
        var seen = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        _db.ImportedEntries.Add(new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = "7",
            UserEmail = "user-account-123",
            WorkDate = new DateOnly(2024, 3, 15),
            TimeSpentSeconds = 3600,
            Description = "Work on issue 100",
            Status = ImportStatus.Mapped,
            SourceUpdatedAt = seen,
        });
        await _db.SaveChangesAsync();

        SetupTempoResponse([MakeWorklog(7, seconds: 99999, updatedAt: seen)]);

        var result = await _sut.ImportIncrementalAsync();

        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Refreshed);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(3600, entry.TimeSpentSeconds);         // Tempo's timestamp is authoritative
        Assert.Equal(ImportStatus.Mapped, entry.Status);
    }

    [Fact]
    public async Task ImportIncrementalAsync_AdvancesWatermarkOnSuccess()
    {
        var source = await SeedSourceAsync();
        var before = DateTimeOffset.UtcNow.AddDays(-1);
        source.LastPolledAt = before;
        await _db.SaveChangesAsync();

        SetupTempoResponse([]);

        await _sut.ImportIncrementalAsync();

        var reloaded = await _db.ImportSources.SingleAsync(s => s.Id == source.Id);
        Assert.NotNull(reloaded.LastPolledAt);
        Assert.True(reloaded.LastPolledAt > before);
    }

    [Fact]
    public async Task ImportIncrementalAsync_KeepsWatermarkWhenFetchFails()
    {
        var source = await SeedSourceAsync();
        var before = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
        source.LastPolledAt = before;
        await _db.SaveChangesAsync();

        SetupTempoFailure();

        var result = await _sut.ImportIncrementalAsync();

        Assert.Equal(0, result.Imported);
        var reloaded = await _db.ImportSources.SingleAsync(s => s.Id == source.Id);
        Assert.Equal(before, reloaded.LastPolledAt);   // next run retries the same span
    }

    [Fact]
    public async Task ImportIncrementalAsync_RecordsUpdatedAtOnImport()
    {
        var source = await SeedSourceAsync();
        var updated = new DateTimeOffset(2026, 9, 2, 9, 30, 0, TimeSpan.Zero);
        SetupTempoResponse([MakeWorklog(8, issueId: 100, updatedAt: updated, createdAt: updated)]);
        SetupJiraIssue(100, "PROJ-1", "PROJ");

        await _sut.ImportIncrementalAsync();

        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(updated, entry.SourceUpdatedAt);
    }

    public void Dispose() => _db.Dispose();
}
