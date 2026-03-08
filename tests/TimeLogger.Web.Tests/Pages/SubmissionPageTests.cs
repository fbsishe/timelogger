using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class SubmissionPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<ISubmissionService> _submissionServiceMock = new();

    public SubmissionPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_submissionServiceMock.Object);

        // Default empty state — individual tests can override
        _submissionServiceMock.Setup(s => s.GetReadyToSubmitAsync(default)).ReturnsAsync([]);
        _submissionServiceMock.Setup(s => s.GetSubmittedCountAsync(default)).ReturnsAsync(0);
        _submissionServiceMock.Setup(s => s.GetFailedCountAsync(default)).ReturnsAsync(0);
        _submissionServiceMock.Setup(s => s.GetRecentAsync(It.IsAny<int>(), default)).ReturnsAsync([]);

        AddAuthorization().SetAuthorized("test@example.com");

        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudBlazor requires a popover provider in the render tree
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task ShowsNoEntriesAlert_WhenNothingPending()
    {
        var cut = Render<Submission>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("No entries are ready for submission", cut.Markup);
    }

    [Fact]
    public async Task ShowsReadyEntries_WhenPresent()
    {
        var readyEntries = new List<ReadyEntryItem>
        {
            new(1, new DateOnly(2024, 1, 15), "Tempo", "PROJ-1", "Backend work", 2.0, "Dev User", "Dev Task"),
        };
        _submissionServiceMock.Setup(s => s.GetReadyToSubmitAsync(default)).ReturnsAsync(readyEntries);
        _submissionServiceMock.Setup(s => s.GetSubmittedCountAsync(default)).ReturnsAsync(5);

        var cut = Render<Submission>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("PROJ-1", cut.Markup);
        Assert.Contains("Backend work", cut.Markup);
        Assert.Contains("Dev Task", cut.Markup);
    }

    [Fact]
    public async Task ShowsStatCounters()
    {
        _submissionServiceMock.Setup(s => s.GetSubmittedCountAsync(default)).ReturnsAsync(42);
        _submissionServiceMock.Setup(s => s.GetFailedCountAsync(default)).ReturnsAsync(3);

        var cut = Render<Submission>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("42", cut.Markup);
        Assert.Contains("3", cut.Markup);
    }

    [Fact]
    public async Task ShowsSubmissionHistory_WhenPresent()
    {
        var history = new List<SubmissionHistoryItem>
        {
            new(1, "PROJ-1", "PROJ", "Backend work", new DateOnly(2024, 1, 15),
                2.0, "Tempo", "Dev User", SubmissionStatus.Success,
                DateTimeOffset.UtcNow.AddHours(-1), 1, null),
        };
        _submissionServiceMock.Setup(s => s.GetRecentAsync(It.IsAny<int>(), default)).ReturnsAsync(history);

        var cut = Render<Submission>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Recent Submission History", cut.Markup);
        Assert.Contains("PROJ-1", cut.Markup);
    }
}
