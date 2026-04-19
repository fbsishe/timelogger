using TimeLogger.Domain.Entities;

namespace TimeLogger.Domain.Tests;

public class EntityDefaultsTests
{
    [Fact]
    public void ImportedEntry_StatusDefaultsToPending()
    {
        var entry = new ImportedEntry { ExternalId = "x", UserEmail = "a@b.com" };
        Assert.Equal(ImportStatus.Pending, entry.Status);
    }

    [Fact]
    public void MappingRule_IsEnabledDefaultsToTrue()
    {
        var rule = new MappingRule
        {
            Name = "R",
            TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() },
        };
        Assert.True(rule.IsEnabled);
    }

    [Fact]
    public void MappingRule_IncludeIssueKeyInCommentDefaultsToFalse()
    {
        var rule = new MappingRule
        {
            Name = "R",
            TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() },
        };
        Assert.False(rule.IncludeIssueKeyInComment);
    }

    [Fact]
    public void MappingRule_ConditionsInitializedToEmptyCollection()
    {
        var rule = new MappingRule
        {
            Name = "R",
            TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() },
        };
        Assert.NotNull(rule.Conditions);
        Assert.Empty(rule.Conditions);
    }

    [Fact]
    public void MappingRule_SourceTypeDefaultsToNull()
    {
        var rule = new MappingRule
        {
            Name = "R",
            TimelogProject = new TimelogProject { Name = "P", ExternalId = Guid.NewGuid().ToString() },
        };
        Assert.Null(rule.SourceType);
    }

    [Fact]
    public void ImportedEntry_TaskAndProjectDefaultToNull()
    {
        var entry = new ImportedEntry { ExternalId = "x", UserEmail = "a@b.com" };

        Assert.Null(entry.TimelogProjectId);
        Assert.Null(entry.TimelogTaskId);
        Assert.Null(entry.MappingRuleId);
    }
}
