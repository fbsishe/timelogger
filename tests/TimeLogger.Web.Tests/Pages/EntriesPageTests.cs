using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Services;
using TimeLogger.Domain.Entities;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class EntriesPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IEntryService> _entryServiceMock = new();
    private readonly Mock<ITimelogDataService> _timelogDataServiceMock = new();
    private readonly Mock<IAppUserService> _appUserServiceMock = new();

    public EntriesPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_entryServiceMock.Object);
        Services.AddSingleton(_timelogDataServiceMock.Object);
        Services.AddSingleton(_appUserServiceMock.Object);

        _entryServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _entryServiceMock
            .Setup(s => s.GetTotalCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _timelogDataServiceMock
            .Setup(s => s.GetProjectsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _appUserServiceMock
            .Setup(s => s.GetByOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        AddAuthorization().SetAuthorized("test@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    private static EntryListItem MakeEntry(int id, string description, string status = "Pending") =>
        new(id, $"w-{id}", "Tempo", new DateOnly(2026, 7, 1), 2.0,
            "PROJ", $"PROJ-{id}", description, "dev@example.com", status, null);

    [Fact]
    public async Task ShowsEntries_WhenPresent()
    {
        _entryServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeEntry(1, "Backend work"), MakeEntry(2, "Frontend work")]);
        _entryServiceMock
            .Setup(s => s.GetTotalCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var cut = Render<Entries>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Backend work", cut.Markup);
        Assert.Contains("Frontend work", cut.Markup);
        Assert.Contains("2 total entries", cut.Markup);
    }

    [Fact]
    public async Task ShowsExportCsvButton()
    {
        _entryServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeEntry(1, "Some work")]);

        var cut = Render<Entries>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Export CSV", cut.Markup);
    }

    [Fact]
    public async Task StatusFilter_ChipsAreRendered()
    {
        var cut = Render<Entries>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        foreach (var status in new[] { "All", "Pending", "Failed", "Mapped", "Submitted", "Conflict", "Ignored" })
            Assert.Contains(status, cut.Markup);
    }
}
