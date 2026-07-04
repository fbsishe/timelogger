using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class SourcesPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IImportSourceService> _sourceServiceMock = new();

    public SourcesPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_sourceServiceMock.Object);

        _sourceServiceMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        AddAuthorization().SetAuthorized("test@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task ShowsSources_WhenPresent()
    {
        _sourceServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ImportSourceDto(1, "Company Tempo", SourceType.Tempo, "https://api.tempo.io/4",
                    "0 6 * * *", true, DateTimeOffset.UtcNow, 120, 5),
            ]);

        var cut = Render<Sources>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Company Tempo", cut.Markup);
        Assert.Contains("Tempo", cut.Markup);
    }

    [Fact]
    public async Task ShowsEmptyState_WhenNoSources()
    {
        var cut = Render<Sources>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        // The add-source affordance must always be present
        Assert.Contains("Source", cut.Markup);
        _sourceServiceMock.Verify(s => s.GetAllAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}

public class ImportHistoryPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IImportSourceService> _sourceServiceMock = new();

    public ImportHistoryPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_sourceServiceMock.Object);

        _sourceServiceMock
            .Setup(s => s.GetImportHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        AddAuthorization().SetAuthorized("test@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task ShowsHistoryRows_WhenPresent()
    {
        _sourceServiceMock
            .Setup(s => s.GetImportHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ImportHistoryEntry(new DateOnly(2026, 7, 1), "Company Tempo", 10, 2, 5, 3, 0),
            ]);

        var cut = Render<ImportHistory>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Company Tempo", cut.Markup);
        Assert.Contains("2026-07-01", cut.Markup);
    }
}
