using Microsoft.EntityFrameworkCore;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Services;

namespace TimeLogger.Infrastructure.Tests.Services;

/// <summary>Covers the "amended after submission" surface: listing, counting and acknowledging.</summary>
public class EntryServiceAmendmentTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EntryService _sut;
    private readonly ImportSource _source;

    public EntryServiceAmendmentTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new EntryService(_db);

        _source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(_source);
        _db.SaveChanges();
    }

    private ImportedEntry Add(
        string externalId,
        DateTimeOffset? amendedAt = null,
        int? amendedSeconds = null,
        string accountId = "acc-1")
    {
        var entry = new ImportedEntry
        {
            ImportSourceId = _source.Id,
            ExternalId = externalId,
            UserEmail = accountId,
            WorkDate = new DateOnly(2026, 9, 2),
            TimeSpentSeconds = 7200,
            Status = ImportStatus.Submitted,
            SourceUpdatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            AmendedAfterSubmissionAt = amendedAt,
            AmendedSourceSeconds = amendedSeconds,
        };
        _db.ImportedEntries.Add(entry);
        _db.SaveChanges();
        return entry;
    }

    [Fact]
    public async Task GetAmendedAfterSubmissionAsync_ReturnsOnlyFlaggedEntries()
    {
        Add("1");
        Add("2", amendedAt: DateTimeOffset.UtcNow, amendedSeconds: 18000);

        var result = await _sut.GetAmendedAfterSubmissionAsync();

        var item = Assert.Single(result);
        Assert.Equal("2", item.ExternalId);
        Assert.True(item.IsAmendedAfterSubmission);
    }

    [Fact]
    public async Task GetAmendedAfterSubmissionAsync_ExposesHoursAndDelta()
    {
        Add("1", amendedAt: DateTimeOffset.UtcNow, amendedSeconds: 18000);

        var item = Assert.Single(await _sut.GetAmendedAfterSubmissionAsync());

        Assert.Equal(2.0, item.Hours);                 // what we submitted
        Assert.Equal(5.0, item.AmendedSourceHours);    // what the source says now
        Assert.Equal(3.0, item.AmendedHoursDelta);
    }

    [Fact]
    public async Task AmendedHoursDelta_IsNegativeWhenSourceHoursWereReduced()
    {
        Add("1", amendedAt: DateTimeOffset.UtcNow, amendedSeconds: 1800);

        var item = Assert.Single(await _sut.GetAmendedAfterSubmissionAsync());

        Assert.Equal(-1.5, item.AmendedHoursDelta);
    }

    [Fact]
    public async Task GetAmendedAfterSubmissionAsync_RespectsAccountFilter()
    {
        Add("1", amendedAt: DateTimeOffset.UtcNow, amendedSeconds: 18000, accountId: "acc-1");
        Add("2", amendedAt: DateTimeOffset.UtcNow, amendedSeconds: 18000, accountId: "acc-2");

        var result = await _sut.GetAmendedAfterSubmissionAsync("acc-2");

        Assert.Single(result);
        Assert.Equal(1, await _sut.GetAmendedAfterSubmissionCountAsync("acc-2"));
        Assert.Equal(2, await _sut.GetAmendedAfterSubmissionCountAsync());
    }

    [Fact]
    public async Task AcknowledgeAmendmentAsync_ClearsFlagAndMarksSourceTimestampSeen()
    {
        var amendedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var entry = Add("1", amendedAt: amendedAt, amendedSeconds: 18000);

        await _sut.AcknowledgeAmendmentAsync(entry.Id);

        var reloaded = await _db.ImportedEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Null(reloaded.AmendedAfterSubmissionAt);
        Assert.Null(reloaded.AmendedSourceSeconds);
        Assert.Null(reloaded.AmendmentReportedAt);

        // Critical: the pull must not re-raise it on the next run
        Assert.Equal(amendedAt, reloaded.SourceUpdatedAt);

        // ...and the entry itself is untouched — Timelog still holds what we sent
        Assert.Equal(7200, reloaded.TimeSpentSeconds);
        Assert.Equal(ImportStatus.Submitted, reloaded.Status);
        Assert.Equal(0, await _sut.GetAmendedAfterSubmissionCountAsync());
    }

    [Fact]
    public async Task AcknowledgeAmendmentAsync_ThrowsForUnknownEntry()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcknowledgeAmendmentAsync(9999));
    }

    public void Dispose() => _db.Dispose();
}
