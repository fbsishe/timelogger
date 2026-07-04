using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Web.Components.Pages;

namespace TimeLogger.Web.Tests.Pages;

public class MappingRulesPageTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); GC.SuppressFinalize(this); }

    private readonly Mock<IMappingRuleService> _ruleServiceMock = new();
    private readonly Mock<ITimelogDataService> _timelogDataServiceMock = new();
    private readonly Mock<IEntryService> _entryServiceMock = new();
    private readonly Mock<IApplyMappingsService> _applyMappingsMock = new();
    private readonly Mock<IMappingSuggestionService> _suggestionServiceMock = new();

    public MappingRulesPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_ruleServiceMock.Object);
        Services.AddSingleton(_timelogDataServiceMock.Object);
        Services.AddSingleton(_entryServiceMock.Object);
        Services.AddSingleton(_applyMappingsMock.Object);
        Services.AddSingleton(_suggestionServiceMock.Object);

        _ruleServiceMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _timelogDataServiceMock
            .Setup(s => s.GetProjectsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _suggestionServiceMock
            .Setup(s => s.GetSuggestionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        AddAuthorization().SetAuthorized("test@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    private static MappingRuleDto MakeRule(
        int id, string name,
        ConditionCombinator combinator = ConditionCombinator.And,
        string? overtimeTaskName = null) =>
        new(id, name, SourceType.Tempo,
            [new MappingRuleConditionDto("ProjectKey", MatchOperator.Equals, "PROJ")],
            combinator,
            1, "Alpha Project", true,
            10, "Dev Task",
            overtimeTaskName is null ? null : 11, overtimeTaskName,
            1, true, false);

    [Fact]
    public async Task ShowsRules_WhenPresent()
    {
        _ruleServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeRule(1, "My Tempo Rule")]);

        var cut = Render<MappingRules>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("My Tempo Rule", cut.Markup);
        Assert.Contains("Alpha Project", cut.Markup);
    }

    [Fact]
    public async Task ShowsSuggestionsPanel_WhenSuggestionsExist()
    {
        _suggestionServiceMock
            .Setup(s => s.GetSuggestionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MappingSuggestionDto(
                "customfield_10501", "APHJ / 5983 Service Requests", 4, "APHJ",
                7, "APHJ / 5983 Service Requests", true)]);

        var cut = Render<MappingRules>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Suggested mappings", cut.Markup);
        Assert.Contains("APHJ / 5983 Service Requests", cut.Markup);
        Assert.Contains("Create mapping", cut.Markup);
    }

    [Fact]
    public async Task HidesSuggestionsPanel_WhenNoSuggestions()
    {
        var cut = Render<MappingRules>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.DoesNotContain("Suggested mappings", cut.Markup);
    }

    [Fact]
    public async Task ShowsOrCombinator_BetweenConditions()
    {
        var rule = new MappingRuleDto(1, "Or Rule", null,
            [
                new MappingRuleConditionDto("ProjectKey", MatchOperator.Equals, "A"),
                new MappingRuleConditionDto("ProjectKey", MatchOperator.Equals, "B"),
            ],
            ConditionCombinator.Or,
            1, "Alpha Project", true, null, null, null, null, 1, true, false);
        _ruleServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);

        var cut = Render<MappingRules>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains(">OR", cut.Markup.Replace("\n", "").Replace("  ", ""));
    }

    [Fact]
    public async Task ShowsOvertimeTaskChip_WhenConfigured()
    {
        _ruleServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeRule(1, "OT Rule", overtimeTaskName: "Dev Task (overtime)")]);

        var cut = Render<MappingRules>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("OT: Dev Task (overtime)", cut.Markup);
    }
}
