using TimeLogger.Application.Mapping;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Domain.Tests;

public class MappingResultTests
{
    private static MappingRule MakeRule() => new()
    {
        Name = "Test Rule",
        TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() },
    };

    private static TimelogProject MakeProject() =>
        new() { Name = "Project A", ExternalId = Guid.NewGuid().ToString() };

    private static TimelogTask MakeTask() =>
        new() { Name = "Task A", ExternalId = Guid.NewGuid().ToString(), ApiTaskId = 42, TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() } };

    [Fact]
    public void Matched_SetsIsMatchedTrue()
    {
        var result = MappingResult.Matched(MakeRule(), MakeProject(), null);
        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Matched_ExposesRuleAndProject()
    {
        var rule = MakeRule();
        var project = MakeProject();

        var result = MappingResult.Matched(rule, project, null);

        Assert.Same(rule, result.MatchedRule);
        Assert.Same(project, result.Project);
    }

    [Fact]
    public void Matched_WithTask_ExposesTask()
    {
        var task = MakeTask();
        var result = MappingResult.Matched(MakeRule(), MakeProject(), task);
        Assert.Same(task, result.Task);
    }

    [Fact]
    public void Matched_WithNullTask_TaskIsNull()
    {
        var result = MappingResult.Matched(MakeRule(), MakeProject(), null);
        Assert.Null(result.Task);
    }

    [Fact]
    public void Unmatched_SetsIsMatchedFalse()
    {
        var result = MappingResult.Unmatched();
        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Unmatched_AllReferencesAreNull()
    {
        var result = MappingResult.Unmatched();

        Assert.Null(result.MatchedRule);
        Assert.Null(result.Project);
        Assert.Null(result.Task);
    }
}
