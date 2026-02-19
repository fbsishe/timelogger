using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Tests.Timelog;

public class TimelogSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITimelogApiClient> _apiClientMock;
    private readonly TimelogSyncService _sut;

    public TimelogSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _apiClientMock = new Mock<ITimelogApiClient>();
        _sut = new TimelogSyncService(_apiClientMock.Object, _db, NullLogger<TimelogSyncService>.Instance);
    }

    [Fact]
    public async Task SyncAsync_InsertsNewProjectsAndTasks()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetProjectsAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogProjectDto>
            {
                Data =
                [
                    new TimelogProjectDto { Id = "proj-guid-1", ProjectId = 42, Name = "Alpha Project" },
                ]
            });

        _apiClientMock
            .Setup(c => c.GetTasksByProjectIdAsync(42, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogTaskDto>
            {
                Data =
                [
                    new TimelogTaskDto { Id = "task-guid-1", TaskId = 101, Name = "Development", ProjectId = 42 },
                    new TimelogTaskDto { Id = "task-guid-2", TaskId = 102, Name = "Testing", ProjectId = 42 },
                ]
            });

        // Act
        await _sut.SyncAsync();

        // Assert
        var project = await _db.TimelogProjects.SingleAsync();
        Assert.Equal("proj-guid-1", project.ExternalId);
        Assert.Equal("Alpha Project", project.Name);
        Assert.True(project.IsActive);

        var tasks = await _db.TimelogTasks.ToListAsync();
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, t => t.ExternalId == "task-guid-1" && t.Name == "Development");
        Assert.Contains(tasks, t => t.ExternalId == "task-guid-2" && t.Name == "Testing");
    }

    [Fact]
    public async Task SyncAsync_UpdatesExistingProject()
    {
        // Arrange — seed existing project with old name
        _db.TimelogProjects.Add(new TimelogProject
        {
            ExternalId = "proj-guid-1",
            Name = "Old Name",
            IsActive = true,
            LastSyncedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        _apiClientMock
            .Setup(c => c.GetProjectsAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogProjectDto>
            {
                Data = [new TimelogProjectDto { Id = "proj-guid-1", ProjectId = 42, Name = "New Name" }]
            });

        _apiClientMock
            .Setup(c => c.GetTasksByProjectIdAsync(42, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogTaskDto> { Data = [] });

        // Act
        await _sut.SyncAsync();

        // Assert
        var project = await _db.TimelogProjects.SingleAsync();
        Assert.Equal("New Name", project.Name);
    }

    [Fact]
    public async Task SyncAsync_MarksProjectInactiveWhenRemovedFromApi()
    {
        // Arrange — seed two projects; API only returns one
        _db.TimelogProjects.AddRange(
            new TimelogProject { ExternalId = "proj-1", Name = "Active", IsActive = true, LastSyncedAt = DateTimeOffset.UtcNow },
            new TimelogProject { ExternalId = "proj-2", Name = "Gone", IsActive = true, LastSyncedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        _apiClientMock
            .Setup(c => c.GetProjectsAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogProjectDto>
            {
                Data = [new TimelogProjectDto { Id = "proj-1", ProjectId = 1, Name = "Active" }]
            });

        _apiClientMock
            .Setup(c => c.GetTasksByProjectIdAsync(1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogTaskDto> { Data = [] });

        // Act
        await _sut.SyncAsync();

        // Assert
        var gone = await _db.TimelogProjects.FirstAsync(p => p.ExternalId == "proj-2");
        Assert.False(gone.IsActive);
    }

    [Fact]
    public async Task GetLastSyncedAtAsync_ReturnsNullWhenNoProjectsExist()
    {
        var result = await _sut.GetLastSyncedAtAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastSyncedAtAsync_ReturnsMostRecentTimestamp()
    {
        var earlier = DateTimeOffset.UtcNow.AddHours(-2);
        var later = DateTimeOffset.UtcNow.AddHours(-1);

        _db.TimelogProjects.AddRange(
            new TimelogProject { ExternalId = "p1", Name = "P1", LastSyncedAt = earlier },
            new TimelogProject { ExternalId = "p2", Name = "P2", LastSyncedAt = later });
        await _db.SaveChangesAsync();

        var result = await _sut.GetLastSyncedAtAsync();

        Assert.NotNull(result);
        Assert.Equal(later, result.Value, TimeSpan.FromSeconds(1));
    }

    public void Dispose() => _db.Dispose();
}
