using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Services;
using TimeLogger.Infrastructure.Timelog;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Tests.Services;

public class AppUserServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITimelogApiClient> _apiClientMock;
    private readonly AppUserService _sut;

    public AppUserServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _apiClientMock = new Mock<ITimelogApiClient>();
        _apiClientMock
            .Setup(c => c.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TafListResponse<TimelogUserDto>());
        _sut = new AppUserService(_db, _apiClientMock.Object,
            new Mock<TimeLogger.Application.Services.IAuditLogService>().Object,
            NullLogger<AppUserService>.Instance);
    }

    [Fact]
    public async Task EnsureUserAsync_CreatesNewUserWhenNotFound()
    {
        var user = await _sut.EnsureUserAsync("oid-1", "new@example.com", "New User");

        Assert.Equal("oid-1", user.EntraObjectId);
        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("New User", user.DisplayName);
        Assert.Equal(AppRole.User, user.Role);
        Assert.Equal(1, await _db.AppUsers.CountAsync());
    }

    [Fact]
    public async Task EnsureUserAsync_ReturnsExistingUserFoundByOid()
    {
        _db.AppUsers.Add(new AppUser
        {
            EntraObjectId = "oid-1",
            Email = "existing@example.com",
            DisplayName = "Existing User",
            Role = AppRole.Manager,
        });
        await _db.SaveChangesAsync();

        var user = await _sut.EnsureUserAsync("oid-1", "existing@example.com", "Existing User");

        Assert.Equal("oid-1", user.EntraObjectId);
        Assert.Equal(AppRole.Manager, user.Role);
        Assert.Equal(1, await _db.AppUsers.CountAsync());
    }

    [Fact]
    public async Task EnsureUserAsync_UpdatesLastLoginAtOnExistingUser()
    {
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        _db.AppUsers.Add(new AppUser
        {
            EntraObjectId = "oid-1",
            Email = "user@example.com",
            DisplayName = "User",
            LastLoginAt = past,
        });
        await _db.SaveChangesAsync();

        await _sut.EnsureUserAsync("oid-1", "user@example.com", "User");

        var updated = await _db.AppUsers.FirstAsync();
        Assert.True(updated.LastLoginAt > past);
    }

    [Fact]
    public async Task EnsureUserAsync_UpdatesEmailAndDisplayNameWhenChanged()
    {
        _db.AppUsers.Add(new AppUser
        {
            EntraObjectId = "oid-1",
            Email = "old@example.com",
            DisplayName = "Old Name",
        });
        await _db.SaveChangesAsync();

        var user = await _sut.EnsureUserAsync("oid-1", "new@example.com", "New Name");

        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("New Name", user.DisplayName);
    }

    [Fact]
    public async Task EnsureUserAsync_UpgradesSeedPendingUser()
    {
        _db.AppUsers.Add(new AppUser
        {
            EntraObjectId = "seed-pending",
            Email = "admin@example.com",
            DisplayName = "Seeded Admin",
            Role = AppRole.Admin,
        });
        await _db.SaveChangesAsync();

        var user = await _sut.EnsureUserAsync("real-oid-abc", "admin@example.com", "Real Admin");

        Assert.Equal("real-oid-abc", user.EntraObjectId);
        Assert.Equal("Real Admin", user.DisplayName);
        Assert.Equal(AppRole.Admin, user.Role);
        Assert.Equal(1, await _db.AppUsers.CountAsync());
    }

    [Fact]
    public async Task EnsureUserAsync_CreatesNewUserWhenEmailMatchesNonSeedUser()
    {
        _db.AppUsers.Add(new AppUser
        {
            EntraObjectId = "other-oid",
            Email = "shared@example.com",
            DisplayName = "Other",
        });
        await _db.SaveChangesAsync();

        var user = await _sut.EnsureUserAsync("new-oid", "shared@example.com", "New Person");

        Assert.Equal("new-oid", user.EntraObjectId);
        Assert.Equal(2, await _db.AppUsers.CountAsync());
    }

    public void Dispose() => _db.Dispose();
}
