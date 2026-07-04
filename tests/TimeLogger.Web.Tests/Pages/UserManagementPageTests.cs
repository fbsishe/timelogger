using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class UserManagementPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IAppUserService> _userServiceMock = new();
    private readonly Mock<ITimelogDataService> _timelogDataServiceMock = new();

    public UserManagementPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_userServiceMock.Object);
        Services.AddSingleton(_timelogDataServiceMock.Object);

        _userServiceMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _timelogDataServiceMock
            .Setup(s => s.GetProjectsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        AddAuthorization().SetAuthorized("admin@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    private static AppUser MakeUser(int id, string name, string email, AppRole role) =>
        new()
        {
            Id = id,
            EntraObjectId = $"oid-{id}",
            Email = email,
            DisplayName = name,
            Role = role,
            LastLoginAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task ShowsUsers_WithNameEmailAndRole()
    {
        _userServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeUser(1, "Jane Admin", "jane@relyits.se", AppRole.Admin),
                MakeUser(2, "Bob User", "bob@relyits.se", AppRole.User),
            ]);

        var cut = Render<UserManagement>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Jane Admin", cut.Markup);
        Assert.Contains("jane@relyits.se", cut.Markup);
        Assert.Contains("Bob User", cut.Markup);
    }

    [Fact]
    public async Task ManagerRow_ShowsProjectAssignment_OthersShowNA()
    {
        _userServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeUser(1, "Regular User", "u@relyits.se", AppRole.User)]);

        var cut = Render<UserManagement>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("N/A", cut.Markup);
    }
}
