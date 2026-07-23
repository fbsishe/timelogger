using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Jobs;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Tests.Jobs;

public class SubmissionReportBuilderTests
{
    private static AutoSubmitReportData MakeData(
        IReadOnlyList<SubmittedGroup>? submitted = null,
        int duplicates = 0, int failed = 0, string? firstError = null,
        int conflicts = 0, int pending = 0, int needsTask = 0) =>
        new(
            LocalRunTime: new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.FromHours(3)),
            Submitted: submitted ?? [],
            DuplicateCount: duplicates,
            FailedCount: failed,
            FirstError: firstError,
            ConflictCount: conflicts,
            PendingUnmappedCount: pending,
            NeedsTaskCount: needsTask,
            NewEntriesSinceLastRun: 0);

    [Fact]
    public void Build_ListsSubmittedHoursPerEmployeeAndProject()
    {
        var report = SubmissionReportBuilder.Build(MakeData(submitted:
        [
            new SubmittedGroup("Jane Doe", "Alpha Project", 3, 6.0),
            new SubmittedGroup("Bob", "Beta Project", 2, 3.5),
        ]));

        Assert.Contains("Submitted 9.5h across 5 entries", report);
        Assert.Contains("Jane Doe → Alpha Project: 6h (3)", report);
        Assert.Contains("Bob → Beta Project: 3.5h (2)", report);
    }

    [Fact]
    public void Build_HighlightsConflictsFailuresAndUnmapped()
    {
        var report = SubmissionReportBuilder.Build(MakeData(
            failed: 2, firstError: "500: upstream down",
            conflicts: 3, pending: 4, needsTask: 1));

        Assert.Contains("Needs attention", report);
        Assert.Contains("3 conflicting registrations", report);
        Assert.Contains("2 failed submissions", report);
        Assert.Contains("500: upstream down", report);
        Assert.Contains("4 unmapped entries", report);
        Assert.Contains("1 mapped entry missing a Timelog task", report);
    }

    [Fact]
    public void Build_AllClear_WhenNothingHappenedAndNothingOutstanding()
    {
        var report = SubmissionReportBuilder.Build(MakeData());

        Assert.Contains("No entries were submitted this run", report);
        Assert.Contains("All clear", report);
    }

    [Fact]
    public void Build_MentionsDuplicates()
    {
        var report = SubmissionReportBuilder.Build(MakeData(duplicates: 2));

        Assert.Contains("2 entries were already in Timelog", report);
    }
}

public class AutoSubmitShouldSendTests
{
    private static DateTimeOffset At(DayOfWeek day, int hour)
    {
        // 2026-07-06 is a Monday
        var monday = new DateTimeOffset(2026, 7, 6, hour, 0, 0, TimeSpan.FromHours(3));
        return monday.AddDays(((int)day - (int)DayOfWeek.Monday + 7) % 7);
    }

    [Fact]
    public void Weekday8am_AlwaysSends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendReport(At(DayOfWeek.Monday, 8), anythingNew: false));

    [Fact]
    public void Weekday1pm_WithoutNews_Suppressed() =>
        Assert.False(AutoSubmitReportJob.ShouldSendReport(At(DayOfWeek.Wednesday, 13), anythingNew: false));

    [Fact]
    public void Weekday5pm_WithNews_Sends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendReport(At(DayOfWeek.Friday, 17), anythingNew: true));

    [Fact]
    public void Weekend8am_WithoutNews_Suppressed() =>
        Assert.False(AutoSubmitReportJob.ShouldSendReport(At(DayOfWeek.Saturday, 8), anythingNew: false));

    [Fact]
    public void Weekend8am_WithNews_Sends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendReport(At(DayOfWeek.Sunday, 8), anythingNew: true));
}

public class AutoSubmitReportJobTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITimelogSubmissionService> _submitterMock = new();
    private readonly Mock<IJobHealthService> _jobHealthMock = new();
    private readonly Mock<ISlackMessageSender> _slackMock = new();

    public AutoSubmitReportJobTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _slackMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private AutoSubmitReportJob CreateSut() =>
        new(_db, _submitterMock.Object, _jobHealthMock.Object, _slackMock.Object,
            Options.Create(new AutoSubmitOptions { TimeZone = "UTC" }),
            NullLogger<AutoSubmitReportJob>.Instance);

    private async Task SeedEntryAsync(ImportStatus status, DateTimeOffset importedAt)
    {
        var source = await _db.ImportSources.FirstOrDefaultAsync();
        if (source is null)
        {
            source = new ImportSource { Name = "S", SourceType = SourceType.Tempo };
            _db.ImportSources.Add(source);
            await _db.SaveChangesAsync();
        }

        _db.ImportedEntries.Add(new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "acc-1",
            WorkDate = new DateOnly(2026, 7, 3),
            TimeSpentSeconds = 3600,
            Status = status,
            ImportedAt = importedAt,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Execute_RunsSubmissionAndRecordsSuccess()
    {
        var job = CreateSut();

        await job.ExecuteAsync();

        _submitterMock.Verify(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        _jobHealthMock.Verify(h => h.RecordSuccessAsync(AutoSubmitReportJob.JobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_NewEntriesSinceLastRun_SendsReport()
    {
        // A previous run happened an hour ago; a new entry arrived after it
        _db.JobExecutions.Add(new JobExecution
        {
            JobName = AutoSubmitReportJob.JobId,
            ExecutedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Succeeded = true,
        });
        await _db.SaveChangesAsync();
        await SeedEntryAsync(ImportStatus.Pending, importedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await CreateSut().ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(It.Is<string>(t => t.Contains("unmapped")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_NothingNew_OffPeakRun_SuppressesReport()
    {
        // Entry imported BEFORE the previous run — nothing new since
        await SeedEntryAsync(ImportStatus.Conflict, importedAt: DateTimeOffset.UtcNow.AddHours(-3));
        _db.JobExecutions.Add(new JobExecution
        {
            JobName = AutoSubmitReportJob.JobId,
            ExecutedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Succeeded = true,
        });
        await _db.SaveChangesAsync();

        await CreateSut().ExecuteAsync();

        // This test runs at an arbitrary wall-clock hour; the suppression branch
        // is only guaranteed outside the 8:00 UTC weekday heartbeat window.
        var nowUtc = DateTimeOffset.UtcNow;
        var isHeartbeatWindow = nowUtc.Hour == 8
            && nowUtc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        if (!isHeartbeatWindow)
            _slackMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_OnFailure_RecordsFailureAndRethrows()
    {
        _submitterMock
            .Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ExecuteAsync());

        _jobHealthMock.Verify(h => h.RecordFailureAsync(
            AutoSubmitReportJob.JobId, "boom", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose() => _db.Dispose();
}
