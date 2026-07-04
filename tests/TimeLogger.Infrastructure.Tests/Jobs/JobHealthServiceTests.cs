using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeLogger.Application.Services;
using TimeLogger.Infrastructure.Jobs;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Tests.Jobs;

public class JobHealthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IJobFailureNotifier> _notifierMock = new();

    public JobHealthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    private JobHealthService CreateSut(int threshold = 3) =>
        new(
            _db,
            _notifierMock.Object,
            Options.Create(new JobHealthOptions { ConsecutiveFailureThreshold = threshold }),
            NullLogger<JobHealthService>.Instance);

    [Fact]
    public async Task RecordFailureAsync_PersistsRunAndNotifies()
    {
        var sut = CreateSut();

        await sut.RecordFailureAsync("tempo-pull", "API unreachable");

        var run = await _db.JobExecutions.SingleAsync();
        Assert.Equal("tempo-pull", run.JobName);
        Assert.False(run.Succeeded);
        Assert.Equal("API unreachable", run.ErrorMessage);
        _notifierMock.Verify(
            n => n.NotifyAsync("tempo-pull", "API unreachable", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordSuccessAsync_DoesNotNotify()
    {
        var sut = CreateSut();

        await sut.RecordSuccessAsync("tempo-pull");

        var run = await _db.JobExecutions.SingleAsync();
        Assert.True(run.Succeeded);
        _notifierMock.Verify(
            n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUnhealthyJobsAsync_FlagsJobAtThreshold()
    {
        var sut = CreateSut(threshold: 3);
        await sut.RecordFailureAsync("tempo-pull", "e1");
        await sut.RecordFailureAsync("tempo-pull", "e2");
        await sut.RecordFailureAsync("tempo-pull", "e3");

        var unhealthy = await sut.GetUnhealthyJobsAsync();

        var status = Assert.Single(unhealthy);
        Assert.Equal("tempo-pull", status.JobName);
        Assert.Equal(3, status.ConsecutiveFailures);
        Assert.Equal("e3", status.LastError);
    }

    [Fact]
    public async Task GetUnhealthyJobsAsync_BelowThreshold_NotFlagged()
    {
        var sut = CreateSut(threshold: 3);
        await sut.RecordFailureAsync("tempo-pull", "e1");
        await sut.RecordFailureAsync("tempo-pull", "e2");

        Assert.Empty(await sut.GetUnhealthyJobsAsync());
    }

    [Fact]
    public async Task GetUnhealthyJobsAsync_SuccessResetsStreak()
    {
        var sut = CreateSut(threshold: 2);
        await sut.RecordFailureAsync("tempo-pull", "e1");
        await sut.RecordFailureAsync("tempo-pull", "e2");
        await sut.RecordSuccessAsync("tempo-pull");

        Assert.Empty(await sut.GetUnhealthyJobsAsync());
    }

    [Fact]
    public async Task GetUnhealthyJobsAsync_TracksJobsIndependently()
    {
        var sut = CreateSut(threshold: 2);
        await sut.RecordFailureAsync("tempo-pull", "e1");
        await sut.RecordFailureAsync("tempo-pull", "e2");
        await sut.RecordSuccessAsync("timelog-sync");

        var unhealthy = await sut.GetUnhealthyJobsAsync();

        var status = Assert.Single(unhealthy);
        Assert.Equal("tempo-pull", status.JobName);
    }

    [Fact]
    public async Task RecordFailureAsync_NotifierThrows_StillPersists()
    {
        _notifierMock
            .Setup(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("webhook down"));
        var sut = CreateSut();

        await sut.RecordFailureAsync("tempo-pull", "boom");

        Assert.Equal(1, await _db.JobExecutions.CountAsync());
    }

    public void Dispose() => _db.Dispose();
}
