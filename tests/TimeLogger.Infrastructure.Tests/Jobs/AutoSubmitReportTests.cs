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
        int conflicts = 0, int pending = 0, int needsTask = 0,
        IReadOnlyList<AmendedAfterSubmission>? newlyAmended = null) =>
        new(
            LocalRunTime: new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.FromHours(3)),
            ReportDay: new DateOnly(2026, 7, 5),
            Submitted: submitted ?? [],
            DuplicateCount: duplicates,
            FailedCount: failed,
            FirstError: firstError,
            ConflictCount: conflicts,
            PendingUnmappedCount: pending,
            NeedsTaskCount: needsTask,
            NewlyAmended: newlyAmended ?? []);

    [Fact]
    public void Build_ListsAmendedWorklogsWithHourDelta()
    {
        var report = SubmissionReportBuilder.Build(MakeData(newlyAmended:
        [
            new AmendedAfterSubmission(1, "Jane Doe", new DateOnly(2026, 9, 2), "PROJ-7", 2.0, 5.0),
        ]));

        Assert.Contains("1 worklog amended in the source", report);
        Assert.Contains("Jane Doe, 2026-09-02 (PROJ-7)", report);
        Assert.Contains("submitted 2h, source now says 5h (+3h)", report);
    }

    [Fact]
    public void Build_ShowsNegativeDeltaWhenSourceHoursWereReduced()
    {
        var report = SubmissionReportBuilder.Build(MakeData(newlyAmended:
        [
            new AmendedAfterSubmission(1, "Bob", new DateOnly(2026, 9, 2), null, 5.0, 1.5),
        ]));

        Assert.Contains("submitted 5h, source now says 1.5h (-3.5h)", report);
        Assert.DoesNotContain("()", report);   // no empty issue-key parens
    }

    [Fact]
    public void Build_TruncatesLongAmendmentListsWithACount()
    {
        var many = Enumerable.Range(1, 14)
            .Select(i => new AmendedAfterSubmission(i, $"Person {i:00}", new DateOnly(2026, 9, 2), null, 1.0, 2.0))
            .ToList();

        var report = SubmissionReportBuilder.Build(MakeData(newlyAmended: many));

        Assert.Contains("14 worklogs amended in the source", report);
        Assert.Contains("and 4 more — see the Entries page", report);
        Assert.Contains("Person 01", report);
        Assert.DoesNotContain("Person 11", report);
    }

    [Fact]
    public void Build_OmitsAmendmentSectionWhenThereAreNone()
    {
        var report = SubmissionReportBuilder.Build(MakeData());
        Assert.DoesNotContain("amended in the source", report);
    }

    [Fact]
    public void Build_ListsSubmittedHoursPerEmployeeAndProject()
    {
        var report = SubmissionReportBuilder.Build(MakeData(submitted:
        [
            new SubmittedGroup("Jane Doe", "Alpha Project", 3, 6.0),
            new SubmittedGroup("Bob", "Beta Project", 2, 3.5),
        ]));

        Assert.Contains("daily report for Sun 05 Jul", report);
        Assert.Contains("Submitted 9.5h across 5 entries on Sun 05 Jul", report);
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

        Assert.Contains("No entries were submitted on Sun 05 Jul", report);
        Assert.Contains("All clear", report);
    }

    [Fact]
    public void Build_LeadsWithFailedStepsAndDropsTheAllClear()
    {
        var report = SubmissionReportBuilder.Build(MakeData() with
        {
            StepFailures = [new StepFailure("Pull from Tempo", "HttpRequestException: 503")],
        });

        Assert.Contains("1 step failed — this run is incomplete", report);
        Assert.Contains("Pull from Tempo: HttpRequestException: 503", report);
        Assert.DoesNotContain("All clear", report);
    }

    [Fact]
    public void Build_AllClear_WhenSubmissionsWentThroughAndNothingIsOutstanding()
    {
        var report = SubmissionReportBuilder.Build(MakeData(submitted: [new SubmittedGroup("Bob", "Beta", 1, 2.0)]));
        Assert.Contains("All clear", report);
    }

    [Fact]
    public void BuildErrorAlert_NamesFailedStepsAndRejectedSubmissions()
    {
        var alert = SubmissionReportBuilder.BuildErrorAlert(
            new DateTimeOffset(2026, 7, 6, 13, 0, 0, TimeSpan.FromHours(3)),
            [new StepFailure("Pull from Tempo", "HttpRequestException: 503")],
            failedCount: 2,
            firstError: "400: task closed");

        Assert.Contains("errors in the 13:00 run, Mon 06 Jul", alert);
        Assert.Contains("Pull from Tempo: HttpRequestException: 503", alert);
        Assert.Contains("2 failed submissions — first error: 400: task closed", alert);
        Assert.DoesNotContain("Submitted", alert);
    }

    [Fact]
    public void Build_MentionsDuplicates()
    {
        var report = SubmissionReportBuilder.Build(MakeData(duplicates: 2));

        Assert.Contains("2 entries were already in Timelog", report);
    }
}

public class AutoSubmitScheduleTests
{
    private static DateTimeOffset At(DayOfWeek day, int hour)
    {
        // 2026-07-06 is a Monday
        var monday = new DateTimeOffset(2026, 7, 6, hour, 0, 0, TimeSpan.FromHours(3));
        return monday.AddDays(((int)day - (int)DayOfWeek.Monday + 7) % 7);
    }

    [Fact]
    public void MorningRun_IsTheDigestRun() =>
        Assert.True(AutoSubmitReportJob.IsDigestRun(At(DayOfWeek.Monday, 8), digestHour: 8));

    [Theory]
    [InlineData(13)]
    [InlineData(17)]
    public void DaytimeRuns_AreNotDigestRuns(int hour) =>
        Assert.False(AutoSubmitReportJob.IsDigestRun(At(DayOfWeek.Monday, hour), digestHour: 8));

    [Fact]
    public void WeekdayDigest_AlwaysSends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendDigest(At(DayOfWeek.Monday, 8), anythingToReport: false));

    [Fact]
    public void WeekendDigest_WithoutNews_Suppressed() =>
        Assert.False(AutoSubmitReportJob.ShouldSendDigest(At(DayOfWeek.Saturday, 8), anythingToReport: false));

    [Fact]
    public void WeekendDigest_WithNews_Sends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendDigest(At(DayOfWeek.Sunday, 8), anythingToReport: true));

    [Fact]
    public void WeekendDigest_WithFailedStep_Sends() =>
        Assert.True(AutoSubmitReportJob.ShouldSendDigest(At(DayOfWeek.Sunday, 8), anythingToReport: false, hasFailures: true));
}

public class AutoSubmitReportJobTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITempoImportService> _importerMock = new();
    private readonly Mock<IApplyMappingsService> _mapperMock = new();
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
        _importerMock.Setup(i => i.ImportIncrementalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TempoImportResult(0, 0, 0));
    }

    /// <summary>
    /// The tests run at an arbitrary wall-clock hour, so the digest hour is pinned to the
    /// current UTC hour (morning run) or the one after it (daytime run).
    /// </summary>
    private AutoSubmitReportJob CreateSut(bool digestRun = false) =>
        new(_db, _importerMock.Object, _mapperMock.Object, _submitterMock.Object,
            _jobHealthMock.Object, _slackMock.Object,
            Options.Create(new AutoSubmitOptions
            {
                TimeZone = "UTC",
                DigestHour = digestRun ? DateTimeOffset.UtcNow.Hour : (DateTimeOffset.UtcNow.Hour + 1) % 24,
            }),
            NullLogger<AutoSubmitReportJob>.Instance);

    private static DateTimeOffset StartOfTodayUtc() => new(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

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

    private async Task SeedSubmissionAsync(
        SubmissionStatus status, DateTimeOffset submittedAt, int seconds = 3600, string? error = null)
    {
        await SeedEntryAsync(ImportStatus.Submitted, importedAt: submittedAt.AddMinutes(-1));
        var entry = await _db.ImportedEntries.OrderByDescending(e => e.Id).FirstAsync();
        entry.TimeSpentSeconds = seconds;
        _db.SubmittedEntries.Add(new SubmittedEntry
        {
            ImportedEntryId = entry.Id,
            Status = status,
            SubmittedAt = submittedAt,
            ErrorMessage = error,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<ImportedEntry> SeedAmendedEntryAsync(DateTimeOffset? reportedAt = null)
    {
        var source = await _db.ImportSources.FirstOrDefaultAsync();
        if (source is null)
        {
            source = new ImportSource { Name = "S", SourceType = SourceType.Tempo };
            _db.ImportSources.Add(source);
            await _db.SaveChangesAsync();
        }

        var entry = new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = "acc-1",
            WorkDate = new DateOnly(2026, 9, 2),
            IssueKey = "PROJ-7",
            TimeSpentSeconds = 7200,
            Status = ImportStatus.Submitted,
            ImportedAt = DateTimeOffset.UtcNow.AddDays(-1),
            AmendedAfterSubmissionAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            AmendedSourceSeconds = 18000,
            AmendmentReportedAt = reportedAt,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task Execute_UnreportedAmendment_SendsReportAndStampsIt()
    {
        var entry = await SeedAmendedEntryAsync();

        await CreateSut(digestRun: true).ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("amended in the source")
                               && t.Contains("submitted 2h, source now says 5h (+3h)")),
            It.IsAny<CancellationToken>()), Times.Once);

        var reloaded = await _db.ImportedEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.NotNull(reloaded.AmendmentReportedAt);
    }

    [Fact]
    public async Task Execute_AlreadyReportedAmendment_IsNotMentionedAgain()
    {
        await SeedAmendedEntryAsync(reportedAt: DateTimeOffset.UtcNow.AddHours(-2));

        await CreateSut(digestRun: true).ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("amended in the source")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_SlackSendFails_LeavesAmendmentUnreportedForRetry()
    {
        var entry = await SeedAmendedEntryAsync();
        _slackMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateSut(digestRun: true).ExecuteAsync();

        var reloaded = await _db.ImportedEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Null(reloaded.AmendmentReportedAt);   // gets another chance next run
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
    public async Task Execute_UnreportedAmendment_DaytimeRun_WaitsForTheDigest()
    {
        var entry = await SeedAmendedEntryAsync();

        await CreateSut().ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var reloaded = await _db.ImportedEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Null(reloaded.AmendmentReportedAt);
    }

    [Fact]
    public async Task Execute_DaytimeRun_WithoutErrors_PostsNothing()
    {
        // Plenty happened — new entries, a successful submission, outstanding conflicts —
        // but none of it is an error, so it waits for the morning digest.
        await SeedEntryAsync(ImportStatus.Conflict, importedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await SeedEntryAsync(ImportStatus.Pending, importedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await SeedSubmissionAsync(SubmissionStatus.Success, submittedAt: DateTimeOffset.UtcNow.AddSeconds(1));

        await CreateSut().ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobHealthMock.Verify(h => h.RecordSuccessAsync(AutoSubmitReportJob.JobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DaytimeRun_RejectedSubmission_PostsErrorAlert()
    {
        _submitterMock.Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .Returns(() => SeedSubmissionAsync(
                SubmissionStatus.Failed, DateTimeOffset.UtcNow, error: "400: task closed"));

        await CreateSut().ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("errors in the")
                               && t.Contains("1 failed submission — first error: 400: task closed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DigestRun_ReportsYesterdaysSubmissionsOnly()
    {
        var yesterdayNoon = StartOfTodayUtc().AddHours(-12);
        await SeedSubmissionAsync(SubmissionStatus.Success, yesterdayNoon, seconds: 3 * 3600);
        await SeedSubmissionAsync(SubmissionStatus.Success, yesterdayNoon.AddHours(-1), seconds: 3600);
        await SeedSubmissionAsync(SubmissionStatus.Success, StartOfTodayUtc().AddHours(-36));   // the day before
        await SeedSubmissionAsync(SubmissionStatus.Success, DateTimeOffset.UtcNow.AddSeconds(1));   // this run

        await CreateSut(digestRun: true).ExecuteAsync();

        var yesterday = DateOnly.FromDateTime(yesterdayNoon.UtcDateTime).ToString("ddd dd MMM");
        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains($"daily report for {yesterday}")
                               && t.Contains($"Submitted 4h across 2 entries on {yesterday}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DigestRun_IncludesThisMorningsRejectedSubmissions()
    {
        await SeedSubmissionAsync(SubmissionStatus.Success, StartOfTodayUtc().AddHours(-12));
        _submitterMock.Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .Returns(() => SeedSubmissionAsync(
                SubmissionStatus.Failed, DateTimeOffset.UtcNow, error: "400: task closed"));

        await CreateSut(digestRun: true).ExecuteAsync();

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("daily report for") && t.Contains("1 failed submission")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_OnFailure_RecordsFailureAndRethrows()
    {
        _submitterMock
            .Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<AutoSubmitStepFailedException>(() => CreateSut().ExecuteAsync());

        _jobHealthMock.Verify(h => h.RecordFailureAsync(
            AutoSubmitReportJob.JobId,
            It.Is<string>(m => m.Contains("Submit to Timelog") && m.Contains("boom")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_RunsPullThenMapThenSubmit_InThatOrder()
    {
        var sequence = new List<string>();
        _importerMock.Setup(i => i.ImportIncrementalAsync(It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("pull"))
            .ReturnsAsync(new TempoImportResult(2, 1, 0));
        _mapperMock.Setup(m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("map"))
            .ReturnsAsync(3);
        _submitterMock.Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("submit"))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync();

        Assert.Equal(["pull", "map", "submit"], sequence);
        _jobHealthMock.Verify(h => h.RecordSuccessAsync(
            AutoSubmitReportJob.JobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_PullFails_StillMapsAndSubmits_AndReportsTheFailureToSlack()
    {
        _importerMock.Setup(i => i.ImportIncrementalAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 from Tempo"));

        await Assert.ThrowsAsync<AutoSubmitStepFailedException>(() => CreateSut().ExecuteAsync());

        _mapperMock.Verify(m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        _submitterMock.Verify(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("1 step failed")
                               && t.Contains("Pull from Tempo")
                               && t.Contains("503 from Tempo")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_StepFailure_DaytimeRun_PostsErrorAlert()
    {
        _mapperMock.Setup(m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rule engine broke"));

        await Assert.ThrowsAsync<AutoSubmitStepFailedException>(() => CreateSut().ExecuteAsync());

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("errors in the")
                               && t.Contains("Apply mapping rules") && t.Contains("rule engine broke")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_AllStepsFail_NamesEachOneInASingleReport()
    {
        _importerMock.Setup(i => i.ImportIncrementalAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tempo down"));
        _mapperMock.Setup(m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mapping down"));
        _submitterMock.Setup(s => s.SubmitAllPendingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("timelog down"));

        var ex = await Assert.ThrowsAsync<AutoSubmitStepFailedException>(() => CreateSut().ExecuteAsync());

        Assert.Equal(3, ex.Failures.Count);
        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("3 steps failed")
                               && t.Contains("tempo down")
                               && t.Contains("mapping down")
                               && t.Contains("timelog down")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_StepFailure_DigestRun_LeadsTheDigestWithIt()
    {
        _importerMock.Setup(i => i.ImportIncrementalAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tempo down"));

        await Assert.ThrowsAsync<AutoSubmitStepFailedException>(() => CreateSut(digestRun: true).ExecuteAsync());

        _slackMock.Verify(s => s.SendAsync(
            It.Is<string>(t => t.Contains("daily report for") && t.Contains("1 step failed") && t.Contains("tempo down")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose() => _db.Dispose();
}
