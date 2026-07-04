using Hangfire;
using Microsoft.EntityFrameworkCore;
using Moq;
using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Services;

namespace TimeLogger.Infrastructure.Tests.Services;

public class SubmissionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITimelogSubmissionService> _submitterMock;
    private readonly SubmissionService _sut;

    public SubmissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _submitterMock = new Mock<ITimelogSubmissionService>();
        _sut = new SubmissionService(_db, new Mock<IBackgroundJobClient>().Object, _submitterMock.Object);
    }

    private async Task<ImportedEntry> SeedEntryAsync(ImportStatus status = ImportStatus.Mapped)
    {
        var source = new ImportSource { Name = "S", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();

        var entry = new ImportedEntry
        {
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "dev@example.com",
            WorkDate = new DateOnly(2024, 1, 1),
            TimeSpentSeconds = 3600,
            Status = status,
            ImportSourceId = source.Id,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private async Task<SubmittedEntry> SeedSubmittedEntryAsync(SubmissionStatus status)
    {
        var entry = await SeedEntryAsync();
        var submitted = new SubmittedEntry
        {
            ImportedEntryId = entry.Id,
            Status = status,
        };
        _db.SubmittedEntries.Add(submitted);
        await _db.SaveChangesAsync();
        return submitted;
    }

    // ------------------------------------------------------------------
    // SubmitSelectedAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitSelectedAsync_CountsSucceededFailedSkipped()
    {
        var e1 = await SeedEntryAsync();
        var e2 = await SeedEntryAsync();
        var e3 = await SeedEntryAsync();

        _submitterMock.Setup(s => s.SubmitAsync(e1, It.IsAny<CancellationToken>())).ReturnsAsync(SubmitOutcome.Succeeded);
        _submitterMock.Setup(s => s.SubmitAsync(e2, It.IsAny<CancellationToken>())).ReturnsAsync(SubmitOutcome.Failed);
        _submitterMock.Setup(s => s.SubmitAsync(e3, It.IsAny<CancellationToken>())).ReturnsAsync(SubmitOutcome.Skipped);

        var result = await _sut.SubmitSelectedAsync([e1.Id, e2.Id, e3.Id]);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task SubmitSelectedAsync_ReturnsAllZeroForEmptyList()
    {
        var result = await _sut.SubmitSelectedAsync([]);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task SubmitSelectedAsync_OnlySubmitsRequestedEntries()
    {
        var e1 = await SeedEntryAsync();
        var e2 = await SeedEntryAsync();

        _submitterMock.Setup(s => s.SubmitAsync(e1, It.IsAny<CancellationToken>())).ReturnsAsync(SubmitOutcome.Succeeded);

        var result = await _sut.SubmitSelectedAsync([e1.Id]);

        Assert.Equal(1, result.Succeeded);
        _submitterMock.Verify(s => s.SubmitAsync(e2, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // AcknowledgeFailureAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task AcknowledgeFailureAsync_SetsStatusToAcknowledged()
    {
        var submitted = await SeedSubmittedEntryAsync(SubmissionStatus.Failed);

        await _sut.AcknowledgeFailureAsync(submitted.Id);

        var updated = await _db.SubmittedEntries.FindAsync(submitted.Id);
        Assert.Equal(SubmissionStatus.Acknowledged, updated!.Status);
    }

    [Fact]
    public async Task AcknowledgeFailureAsync_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcknowledgeFailureAsync(9999));
    }

    // ------------------------------------------------------------------
    // AcknowledgeAllFailuresAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task AcknowledgeAllFailuresAsync_SetsAllFailedToAcknowledged()
    {
        var f1 = await SeedSubmittedEntryAsync(SubmissionStatus.Failed);
        var f2 = await SeedSubmittedEntryAsync(SubmissionStatus.Failed);

        await _sut.AcknowledgeAllFailuresAsync();

        var updated1 = await _db.SubmittedEntries.FindAsync(f1.Id);
        var updated2 = await _db.SubmittedEntries.FindAsync(f2.Id);
        Assert.Equal(SubmissionStatus.Acknowledged, updated1!.Status);
        Assert.Equal(SubmissionStatus.Acknowledged, updated2!.Status);
    }

    [Fact]
    public async Task AcknowledgeAllFailuresAsync_IgnoresNonFailedEntries()
    {
        var success = await SeedSubmittedEntryAsync(SubmissionStatus.Success);
        var retrying = await SeedSubmittedEntryAsync(SubmissionStatus.Retrying);

        await _sut.AcknowledgeAllFailuresAsync();

        var updatedSuccess = await _db.SubmittedEntries.FindAsync(success.Id);
        var updatedRetrying = await _db.SubmittedEntries.FindAsync(retrying.Id);
        Assert.Equal(SubmissionStatus.Success, updatedSuccess!.Status);
        Assert.Equal(SubmissionStatus.Retrying, updatedRetrying!.Status);
    }

    // ------------------------------------------------------------------
    // GetSubmissionSummaryAsync
    // ------------------------------------------------------------------

    private async Task SeedSubmissionForSummaryAsync(
        string accountId, string projectName, DateOnly workDate, int seconds,
        SubmissionStatus status = SubmissionStatus.Success)
    {
        var source = await _db.ImportSources.FirstOrDefaultAsync();
        if (source is null)
        {
            source = new ImportSource { Name = "S", SourceType = SourceType.Tempo };
            _db.ImportSources.Add(source);
            await _db.SaveChangesAsync();
        }

        var project = await _db.TimelogProjects.FirstOrDefaultAsync(p => p.Name == projectName);
        if (project is null)
        {
            project = new TimelogProject
            {
                ExternalId = Guid.NewGuid().ToString(),
                Name = projectName,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            _db.TimelogProjects.Add(project);
            await _db.SaveChangesAsync();
        }

        var entry = new ImportedEntry
        {
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = accountId,
            WorkDate = workDate,
            TimeSpentSeconds = seconds,
            Status = ImportStatus.Submitted,
            ImportSourceId = source.Id,
            TimelogProjectId = project.Id,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();

        _db.SubmittedEntries.Add(new SubmittedEntry
        {
            ImportedEntryId = entry.Id,
            Status = status,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSubmissionSummaryAsync_GroupsByEmployeeAndProject()
    {
        _db.EmployeeMappings.Add(new EmployeeMapping { AtlassianAccountId = "acc-1", DisplayName = "Jane" });
        await _db.SaveChangesAsync();

        await SeedSubmissionForSummaryAsync("acc-1", "Alpha", new DateOnly(2026, 6, 10), 3600);
        await SeedSubmissionForSummaryAsync("acc-1", "Alpha", new DateOnly(2026, 6, 11), 7200);
        await SeedSubmissionForSummaryAsync("acc-1", "Beta", new DateOnly(2026, 6, 12), 1800);

        var rows = await _sut.GetSubmissionSummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(2, rows.Count);
        var alpha = rows.Single(r => r.Project == "Alpha");
        Assert.Equal("Jane", alpha.Employee);
        Assert.Equal(2, alpha.EntryCount);
        Assert.Equal(3.0, alpha.Hours);
        var beta = rows.Single(r => r.Project == "Beta");
        Assert.Equal(0.5, beta.Hours);
    }

    [Fact]
    public async Task GetSubmissionSummaryAsync_ExcludesOutOfRangeAndFailed()
    {
        await SeedSubmissionForSummaryAsync("acc-1", "Alpha", new DateOnly(2026, 5, 31), 3600);
        await SeedSubmissionForSummaryAsync("acc-1", "Alpha", new DateOnly(2026, 6, 10), 3600,
            SubmissionStatus.Failed);
        await SeedSubmissionForSummaryAsync("acc-1", "Alpha", new DateOnly(2026, 6, 10), 3600,
            SubmissionStatus.Duplicate);

        var rows = await _sut.GetSubmissionSummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var row = Assert.Single(rows);
        Assert.Equal(1, row.EntryCount);
        Assert.Equal(1.0, row.Hours);
    }

    public void Dispose() => _db.Dispose();
}
