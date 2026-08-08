using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Tests.Timelog;

public class DayReviewServiceTests : IDisposable
{
    private const string AccountId = "acc-123";
    private static readonly DateOnly Date = new(2026, 7, 1);

    private readonly AppDbContext _db;
    private readonly Mock<ITimelogApiClient> _apiClientMock = new();
    private readonly Mock<ITimelogSubmissionService> _submitterMock = new();
    private readonly DayReviewService _sut;

    public DayReviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new DayReviewService(_apiClientMock.Object, _db, _submitterMock.Object,
            NullLogger<DayReviewService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<TimelogTask> SeedTaskAsync(int apiTaskId = 500, string name = "Dev Task")
    {
        var project = new TimelogProject { ExternalId = $"proj-{apiTaskId}", Name = "Proj", LastSyncedAt = DateTimeOffset.UtcNow };
        _db.TimelogProjects.Add(project);
        await _db.SaveChangesAsync();

        var task = new TimelogTask
        {
            ExternalId = $"guid-{apiTaskId}",
            ApiTaskId = apiTaskId,
            Name = name,
            TimelogProjectId = project.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        _db.TimelogTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    private async Task<ImportedEntry> SeedEntryAsync(
        int? timelogTaskId, double hours = 2.0, ImportStatus status = ImportStatus.Submitted, string issueKey = "TL-1")
    {
        var source = await _db.ImportSources.FirstOrDefaultAsync();
        if (source is null)
        {
            source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
            _db.ImportSources.Add(source);
            await _db.SaveChangesAsync();
        }

        var entry = new ImportedEntry
        {
            ExternalId = Guid.NewGuid().ToString(),
            UserEmail = AccountId,
            WorkDate = Date,
            TimeSpentSeconds = (int)(hours * 3600),
            Description = "Work",
            IssueKey = issueKey,
            Status = status,
            ImportSourceId = source.Id,
            TimelogTaskId = timelogTaskId,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private async Task SeedMappingAsync(int timelogUserId = 8)
    {
        _db.EmployeeMappings.Add(new EmployeeMapping
        {
            AtlassianAccountId = AccountId,
            DisplayName = "Dev Developer",
            TimelogUserId = timelogUserId,
        });
        await _db.SaveChangesAsync();
    }

    private void SetupApiReturns(params TimeTrackingItemDto[] items)
    {
        _apiClientMock
            .Setup(c => c.GetTimeTrackingItemsByDateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimeTrackingItemDto>
            {
                Entities = items.Select(i => new TafEntity<TimeTrackingItemDto> { Properties = i }).ToList(),
            });
    }

    private static TimeTrackingItemDto Registration(
        int taskId, double hours, int userId = 8, int regId = 1000, int? approvalStatus = 7) => new()
    {
        TimeRegistrationId = regId,
        TaskId = taskId,
        TaskName = "Dev Task",
        UserId = userId,
        Hours = hours,
        Comment = "Work",
        ApprovalStatus = approvalStatus,
    };

    [Fact]
    public async Task NoEmployeeMapping_ReturnsWarningAndSkipsTimelog()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id);

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        Assert.False(result.TimelogQueried);
        Assert.NotNull(result.Warning);
        Assert.Single(result.Groups);
        _apiClientMock.Verify(
            c => c.GetTimeTrackingItemsByDateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MatchingHours_ProducesMatchGroup()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, hours: 2.0);
        await SeedMappingAsync();
        SetupApiReturns(Registration(500, 2.0));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Match, group.State);
        Assert.Equal("Dev Developer", result.UserDisplay);
        Assert.Equal(2.0, result.OurTotal);
        Assert.Equal(2.0, result.TimelogTotal);
    }

    [Fact]
    public async Task DifferingHours_OneToOne_IsQuickResolvable()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, hours: 2.0);
        await SeedMappingAsync();
        SetupApiReturns(Registration(500, 3.5));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Different, group.State);
        Assert.True(group.CanQuickResolve);
        Assert.Equal(2.0, group.OurHours);
        Assert.Equal(3.5, group.TimelogHours);
    }

    [Fact]
    public async Task DifferingHours_MultipleEntriesOnTask_IsNotQuickResolvable()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, hours: 1.0, issueKey: "TL-1");
        await SeedEntryAsync(task.Id, hours: 1.0, issueKey: "TL-2");
        await SeedMappingAsync();
        SetupApiReturns(Registration(500, 5.0));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Different, group.State);
        Assert.False(group.CanQuickResolve);
    }

    [Fact]
    public async Task MultipleEntriesAndRegistrations_SumsAreCompared()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, hours: 1.0, issueKey: "TL-1");
        await SeedEntryAsync(task.Id, hours: 2.0, issueKey: "TL-2");
        await SeedMappingAsync();
        SetupApiReturns(Registration(500, 1.0, regId: 1), Registration(500, 2.0, regId: 2));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Match, group.State);
    }

    [Fact]
    public async Task NoRegistration_ProducesMissingInTimelog()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, status: ImportStatus.Mapped);
        await SeedMappingAsync();
        SetupApiReturns();

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.MissingInTimelog, group.State);
    }

    [Fact]
    public async Task RegistrationWithoutEntry_ProducesOnlyInTimelog()
    {
        await SeedMappingAsync();
        SetupApiReturns(Registration(999, 4.0));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.OnlyInTimelog, group.State);
        Assert.Equal("Dev Task", group.TaskName);
        Assert.True(group.Registrations[0].IsApproved);
    }

    [Fact]
    public async Task UnmappedEntries_GroupedSeparately()
    {
        await SeedEntryAsync(timelogTaskId: null, status: ImportStatus.Pending);
        await SeedMappingAsync();
        SetupApiReturns();

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Unmapped, group.State);
        Assert.Null(group.ApiTaskId);
    }

    [Fact]
    public async Task IgnoredEntries_AreExcluded()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, status: ImportStatus.Ignored);
        await SeedMappingAsync();
        SetupApiReturns();

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task OtherUsersRegistrations_AreFilteredOut()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id, hours: 2.0, status: ImportStatus.Mapped);
        await SeedMappingAsync(timelogUserId: 8);
        SetupApiReturns(Registration(500, 9.0, userId: 42));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.MissingInTimelog, group.State);
        Assert.Empty(group.Registrations);
    }

    [Fact]
    public async Task ApiFailure_ReturnsWarning_AndMissingBecomesNotComparable()
    {
        var task = await SeedTaskAsync();
        await SeedEntryAsync(task.Id);
        await SeedMappingAsync();
        _apiClientMock
            .Setup(c => c.GetTimeTrackingItemsByDateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await _sut.GetDayReviewAsync(AccountId, Date);

        Assert.False(result.TimelogQueried);
        Assert.NotNull(result.Warning);
        var group = Assert.Single(result.Groups);
        Assert.Equal(DayGroupState.Unmapped, group.State);
    }

    [Fact]
    public async Task ResolveDifference_RecordsConflictAndDelegates()
    {
        var task = await SeedTaskAsync();
        var entry = await SeedEntryAsync(task.Id, hours: 2.0, status: ImportStatus.Submitted);
        _submitterMock
            .Setup(s => s.ResolveConflictAsync(
                It.IsAny<ImportedEntry>(), ConflictResolution.UseOurs, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubmitOutcome.Succeeded);

        var outcome = await _sut.ResolveDifferenceAsync(entry.Id, 4242, 3.5, ConflictResolution.UseOurs);

        Assert.Equal(SubmitOutcome.Succeeded, outcome);
        _submitterMock.Verify(s => s.ResolveConflictAsync(
            It.Is<ImportedEntry>(e => e.Id == entry.Id
                && e.ConflictTimelogRegistrationId == "4242"
                && e.ConflictHoursInTimelog == 3.5),
            ConflictResolution.UseOurs, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveDifference_UnknownEntry_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ResolveDifferenceAsync(9999, 1, 1.0, ConflictResolution.UseOurs));
    }
}
