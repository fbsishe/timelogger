using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Services;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class HomePageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IEntryService> _entryServiceMock = new();
    private readonly Mock<ITimelogDataService> _timelogDataServiceMock = new();
    private readonly Mock<IImportSourceService> _importSourceServiceMock = new();

    public HomePageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_entryServiceMock.Object);
        Services.AddSingleton(_timelogDataServiceMock.Object);
        Services.AddSingleton(_importSourceServiceMock.Object);

        // Provide default empty returns so LoadAsync never crashes on unmocked calls
        _entryServiceMock.Setup(s => s.GetUnmappedAsync(default)).ReturnsAsync([]);
        _timelogDataServiceMock.Setup(s => s.GetLastSyncedAtAsync(default)).ReturnsAsync((DateTimeOffset?)null);
        _timelogDataServiceMock.Setup(s => s.GetProjectsAsync(default)).ReturnsAsync([]);
        _importSourceServiceMock.Setup(s => s.GetAllAsync(default)).ReturnsAsync([]);

        AddAuthorization().SetAuthorized("test@example.com");

        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudBlazor requires a popover provider in the render tree
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void ShowsLoadingSpinner_WhileDataLoads()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<EntryListItem>>();
        _entryServiceMock.Setup(s => s.GetUnmappedAsync(default)).Returns(tcs.Task);

        var cut = Render<Home>();

        Assert.Contains("mud-progress-linear", cut.Markup);
    }

    [Fact]
    public async Task ShowsSuccessAlert_WhenNoUnmappedEntries()
    {
        var cut = Render<Home>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("All entries are mapped", cut.Markup);
    }

    [Fact]
    public async Task ShowsUnmappedEntries_WhenPresent()
    {
        var entries = new List<EntryListItem>
        {
            new(1, "w-1", "Tempo", new DateOnly(2024, 1, 15), 2.0,
                "PROJ", "PROJ-1", "Backend work", "dev@example.com", "Pending", null),
        };
        _entryServiceMock.Setup(s => s.GetUnmappedAsync(default)).ReturnsAsync(entries);
        _timelogDataServiceMock.Setup(s => s.GetLastSyncedAtAsync(default)).ReturnsAsync(DateTimeOffset.UtcNow);

        var cut = Render<Home>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("PROJ-1", cut.Markup);
        Assert.Contains("Backend work", cut.Markup);
        Assert.Contains("Unmapped Entries Requiring Attention", cut.Markup);
    }

    [Fact]
    public async Task ShowsUnmappedCount_InStatCard()
    {
        var entries = new List<EntryListItem>
        {
            new(1, "w-1", "Tempo", new DateOnly(2024, 1, 15), 2.0,
                "PROJ", "PROJ-1", "Work", "dev@example.com", "Pending", null),
            new(2, "w-2", "Tempo", new DateOnly(2024, 1, 16), 1.5,
                "PROJ", "PROJ-2", "More work", "dev@example.com", "Pending", null),
        };
        _entryServiceMock.Setup(s => s.GetUnmappedAsync(default)).ReturnsAsync(entries);

        var cut = Render<Home>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        // Stat card shows count of unmapped entries
        Assert.Contains(">2<", cut.Markup);
    }

    [Fact]
    public async Task ShowsNeverForLastSynced_WhenNull()
    {
        var cut = Render<Home>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Never", cut.Markup);
    }
}
