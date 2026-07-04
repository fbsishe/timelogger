using Microsoft.EntityFrameworkCore;
using Moq;
using TimeLogger.Application.Services;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Services;

namespace TimeLogger.Infrastructure.Tests.Services;

public class AuditLogServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    private AuditLogService CreateSut(string? currentUser = null)
    {
        var provider = new Mock<ICurrentUserProvider>();
        provider
            .Setup(p => p.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);
        return new AuditLogService(_db, provider.Object);
    }

    [Fact]
    public async Task LogAsync_RecordsChangeWithActor()
    {
        var sut = CreateSut("jane@relyits.se");

        await sut.LogAsync("MappingRule", "Created", "My Rule", "some detail");

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("jane@relyits.se", entry.Actor);
        Assert.Equal("MappingRule", entry.Category);
        Assert.Equal("Created", entry.Action);
        Assert.Equal("My Rule", entry.Subject);
        Assert.Equal("some detail", entry.Details);
    }

    [Fact]
    public async Task LogAsync_FallsBackToSystemActor()
    {
        var sut = CreateSut(currentUser: null);

        await sut.LogAsync("ImportSource", "Deleted", "Tempo");

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("system", entry.Actor);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirstAndRespectsLimit()
    {
        var sut = CreateSut("jane@relyits.se");
        await sut.LogAsync("A", "Created", "first");
        await sut.LogAsync("B", "Created", "second");
        await sut.LogAsync("C", "Created", "third");

        var items = await sut.GetRecentAsync(limit: 2);

        Assert.Equal(2, items.Count);
        Assert.Equal("third", items[0].Subject);
        Assert.Equal("second", items[1].Subject);
    }

    public void Dispose() => _db.Dispose();
}
