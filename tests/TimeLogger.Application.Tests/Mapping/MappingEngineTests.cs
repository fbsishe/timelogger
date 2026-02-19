using TimeLogger.Application.Mapping;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Tests.Mapping;

/// <summary>Comprehensive tests for MappingEngine rule evaluation.</summary>
public class MappingEngineTests
{
    private readonly MappingEngine _engine = new();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static ImportSource MakeSource(SourceType type = SourceType.Tempo) =>
        new() { Name = "src", SourceType = type };

    private static TimelogProject MakeProject(int id = 1) =>
        new() { Id = id, ExternalId = $"p-{id}", Name = $"Project {id}", LastSyncedAt = DateTimeOffset.UtcNow };

    private static TimelogTask MakeTask(int id = 1, int projectId = 1) =>
        new() { Id = id, ExternalId = $"t-{id}", Name = $"Task {id}", TimelogProjectId = projectId, LastSyncedAt = DateTimeOffset.UtcNow };

    private static ImportedEntry MakeEntry(
        string? projectKey = "PROJ",
        string? issueKey = "PROJ-1",
        string? userEmail = "dev@example.com",
        string? description = "Some work",
        string? activity = "Development",
        string? metadataJson = null,
        SourceType sourceType = SourceType.Tempo) =>
        new()
        {
            ExternalId = "e-1",
            UserEmail = userEmail ?? "dev@example.com",
            WorkDate = new DateOnly(2024, 1, 1),
            TimeSpentSeconds = 3600,
            ProjectKey = projectKey,
            IssueKey = issueKey,
            Activity = activity,
            Description = description,
            MetadataJson = metadataJson,
            ImportSource = MakeSource(sourceType),
            ImportSourceId = 1,
        };

    private static MappingRule MakeRule(
        string matchField,
        MatchOperator op,
        string matchValue,
        int priority = 10,
        SourceType? sourceType = null,
        int projectId = 1) =>
        new()
        {
            Id = 1,
            Name = "Test Rule",
            MatchField = matchField,
            MatchOperator = op,
            MatchValue = matchValue,
            Priority = priority,
            SourceType = sourceType,
            TimelogProjectId = projectId,
            TimelogProject = MakeProject(projectId),
            IsEnabled = true,
        };

    // ------------------------------------------------------------------
    // Operator: Equals
    // ------------------------------------------------------------------

    [Fact]
    public void Equals_MatchesExactValue() =>
        Assert.True(_engine.Matches(
            MakeRule("ProjectKey", MatchOperator.Equals, "PROJ"),
            MakeEntry(projectKey: "PROJ")));

    [Fact]
    public void Equals_IsCaseInsensitive() =>
        Assert.True(_engine.Matches(
            MakeRule("ProjectKey", MatchOperator.Equals, "proj"),
            MakeEntry(projectKey: "PROJ")));

    [Fact]
    public void Equals_DoesNotMatchPartialValue() =>
        Assert.False(_engine.Matches(
            MakeRule("ProjectKey", MatchOperator.Equals, "PRO"),
            MakeEntry(projectKey: "PROJ")));

    // ------------------------------------------------------------------
    // Operator: Contains
    // ------------------------------------------------------------------

    [Fact]
    public void Contains_MatchesSubstring() =>
        Assert.True(_engine.Matches(
            MakeRule("Description", MatchOperator.Contains, "work"),
            MakeEntry(description: "Some work done")));

    [Fact]
    public void Contains_IsCaseInsensitive() =>
        Assert.True(_engine.Matches(
            MakeRule("Description", MatchOperator.Contains, "WORK"),
            MakeEntry(description: "some work done")));

    [Fact]
    public void Contains_DoesNotMatchMissingSubstring() =>
        Assert.False(_engine.Matches(
            MakeRule("Description", MatchOperator.Contains, "billing"),
            MakeEntry(description: "feature work")));

    // ------------------------------------------------------------------
    // Operator: StartsWith
    // ------------------------------------------------------------------

    [Fact]
    public void StartsWith_MatchesPrefix() =>
        Assert.True(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.StartsWith, "MOBILE"),
            MakeEntry(issueKey: "MOBILE-123")));

    [Fact]
    public void StartsWith_IsCaseInsensitive() =>
        Assert.True(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.StartsWith, "mobile"),
            MakeEntry(issueKey: "MOBILE-123")));

    [Fact]
    public void StartsWith_DoesNotMatchMiddleOfString() =>
        Assert.False(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.StartsWith, "123"),
            MakeEntry(issueKey: "MOBILE-123")));

    // ------------------------------------------------------------------
    // Operator: Regex
    // ------------------------------------------------------------------

    [Fact]
    public void Regex_MatchesPattern() =>
        Assert.True(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.Regex, @"^MOBILE-\d+$"),
            MakeEntry(issueKey: "MOBILE-42")));

    [Fact]
    public void Regex_IsCaseInsensitive() =>
        Assert.True(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.Regex, @"^mobile-\d+$"),
            MakeEntry(issueKey: "MOBILE-42")));

    [Fact]
    public void Regex_DoesNotMatchWhenPatternFails() =>
        Assert.False(_engine.Matches(
            MakeRule("IssueKey", MatchOperator.Regex, @"^BACKEND-\d+$"),
            MakeEntry(issueKey: "MOBILE-42")));

    [Fact]
    public void Regex_MatchesComplexPattern()
    {
        var entry = MakeEntry(description: "Fix bug in payment-service v2.1");
        Assert.True(_engine.Matches(
            MakeRule("Description", MatchOperator.Regex, @"payment.service\s+v\d"),
            entry));
    }

    // ------------------------------------------------------------------
    // Standard fields
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ProjectKey",  "PROJ",             "PROJ")]
    [InlineData("IssueKey",    "PROJ-42",           "PROJ-42")]
    [InlineData("UserEmail",   "dev@example.com",   "dev@example.com")]
    [InlineData("Description", "feature work",      "feature work")]
    [InlineData("Activity",    "Development",       "Development")]
    public void StandardFields_MatchCorrectly(string field, string ruleValue, string entryValue)
    {
        var entry = MakeEntry(
            projectKey: field == "ProjectKey" ? entryValue : "X",
            issueKey: field == "IssueKey" ? entryValue : "X-1",
            userEmail: field == "UserEmail" ? entryValue : "other@example.com",
            description: field == "Description" ? entryValue : "other",
            activity: field == "Activity" ? entryValue : "other");

        Assert.True(_engine.Matches(MakeRule(field, MatchOperator.Equals, ruleValue), entry));
    }

    [Fact]
    public void UnknownField_NeverMatches() =>
        Assert.False(_engine.Matches(
            MakeRule("nonexistentfield", MatchOperator.Equals, "anything"),
            MakeEntry()));

    // ------------------------------------------------------------------
    // Metadata (JSON) fields
    // ------------------------------------------------------------------

    [Fact]
    public void MetadataField_MatchesCustomField()
    {
        var entry = MakeEntry(metadataJson: """{"customfield_10200":"timelog-task-99"}""");
        Assert.True(_engine.Matches(
            MakeRule("metadata.customfield_10200", MatchOperator.Equals, "timelog-task-99"),
            entry));
    }

    [Fact]
    public void MetadataField_IsCaseInsensitiveOnKey()
    {
        var entry = MakeEntry(metadataJson: """{"customfield_10200":"Value"}""");
        Assert.True(_engine.Matches(
            MakeRule("metadata.CUSTOMFIELD_10200", MatchOperator.Equals, "value"),
            entry));
    }

    [Fact]
    public void MetadataField_MatchesTempoAttribute()
    {
        var entry = MakeEntry(metadataJson: """{"attr__WorkType_":"Development"}""");
        Assert.True(_engine.Matches(
            MakeRule("metadata.attr__WorkType_", MatchOperator.Equals, "development"),
            entry));
    }

    [Fact]
    public void MetadataField_ReturnsFalseWhenKeyAbsent()
    {
        var entry = MakeEntry(metadataJson: """{"other_field":"something"}""");
        Assert.False(_engine.Matches(
            MakeRule("metadata.customfield_10200", MatchOperator.Equals, "anything"),
            entry));
    }

    [Fact]
    public void MetadataField_ReturnsFalseWhenNullValue()
    {
        var entry = MakeEntry(metadataJson: """{"customfield_10200":null}""");
        Assert.False(_engine.Matches(
            MakeRule("metadata.customfield_10200", MatchOperator.Equals, "anything"),
            entry));
    }

    [Fact]
    public void MetadataField_ReturnsFalseWhenMetadataNull()
    {
        var entry = MakeEntry(metadataJson: null);
        Assert.False(_engine.Matches(
            MakeRule("metadata.customfield_10200", MatchOperator.Equals, "anything"),
            entry));
    }

    [Fact]
    public void MetadataField_ReturnsFalseWhenMetadataMalformed()
    {
        var entry = MakeEntry(metadataJson: "not-json{{");
        Assert.False(_engine.Matches(
            MakeRule("metadata.customfield_10200", MatchOperator.Equals, "anything"),
            entry));
    }

    // ------------------------------------------------------------------
    // Priority ordering
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReturnsFirstMatchByPriority()
    {
        var projectA = MakeProject(1);
        var projectB = MakeProject(2);

        var rules = new List<MappingRule>
        {
            new()
            {
                Id = 2, Name = "Low priority", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
                Priority = 20, TimelogProjectId = 2, TimelogProject = projectB, IsEnabled = true,
            },
            new()
            {
                Id = 1, Name = "High priority", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
                Priority = 5, TimelogProjectId = 1, TimelogProject = projectA, IsEnabled = true,
            },
        };

        var result = _engine.Evaluate(rules, MakeEntry(projectKey: "PROJ"));

        Assert.True(result.IsMatched);
        Assert.Equal(1, result.Project!.Id); // High priority rule wins
    }

    [Fact]
    public void Evaluate_ReturnsUnmatchedWhenNoRulesMatch()
    {
        var rules = new List<MappingRule>
        {
            new()
            {
                Id = 1, Name = "Rule", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "OTHER",
                Priority = 1, TimelogProjectId = 1, TimelogProject = MakeProject(), IsEnabled = true,
            },
        };

        var result = _engine.Evaluate(rules, MakeEntry(projectKey: "PROJ"));

        Assert.False(result.IsMatched);
        Assert.Null(result.MatchedRule);
    }

    [Fact]
    public void Evaluate_ReturnsUnmatchedWhenRuleListEmpty()
    {
        var result = _engine.Evaluate([], MakeEntry());
        Assert.False(result.IsMatched);
    }

    // ------------------------------------------------------------------
    // Disabled rules
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_SkipsDisabledRules()
    {
        var rules = new List<MappingRule>
        {
            new()
            {
                Id = 1, Name = "Disabled", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
                Priority = 1, TimelogProjectId = 1, TimelogProject = MakeProject(), IsEnabled = false,
            },
        };

        var result = _engine.Evaluate(rules, MakeEntry(projectKey: "PROJ"));
        Assert.False(result.IsMatched);
    }

    // ------------------------------------------------------------------
    // Source-type filtering
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_SkipsRuleWithMismatchedSourceType()
    {
        var rules = new List<MappingRule>
        {
            new()
            {
                Id = 1, Name = "Tempo only", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
                Priority = 1, SourceType = SourceType.Tempo,
                TimelogProjectId = 1, TimelogProject = MakeProject(), IsEnabled = true,
            },
        };

        // Entry from a file upload — should not match Tempo-scoped rule
        var entry = MakeEntry(projectKey: "PROJ", sourceType: SourceType.FileUpload);
        var result = _engine.Evaluate(rules, entry);
        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_AppliesRuleWithNullSourceTypeToAllSources()
    {
        var rules = new List<MappingRule>
        {
            new()
            {
                Id = 1, Name = "All sources", MatchField = "ProjectKey",
                MatchOperator = MatchOperator.Equals, MatchValue = "PROJ",
                Priority = 1, SourceType = null,
                TimelogProjectId = 1, TimelogProject = MakeProject(), IsEnabled = true,
            },
        };

        Assert.True(_engine.Evaluate(rules, MakeEntry(projectKey: "PROJ", sourceType: SourceType.Tempo)).IsMatched);
        Assert.True(_engine.Evaluate(rules, MakeEntry(projectKey: "PROJ", sourceType: SourceType.FileUpload)).IsMatched);
    }

    // ------------------------------------------------------------------
    // Null / empty field values
    // ------------------------------------------------------------------

    [Fact]
    public void Matches_ReturnsFalseWhenFieldValueIsNull()
    {
        // ProjectKey is null — Equals should not match
        var entry = MakeEntry(projectKey: null);
        Assert.False(_engine.Matches(
            MakeRule("ProjectKey", MatchOperator.Equals, "PROJ"),
            entry));
    }

    // ------------------------------------------------------------------
    // MappingResult value object
    // ------------------------------------------------------------------

    [Fact]
    public void MappingResult_Matched_ExposesProjectAndTask()
    {
        var project = MakeProject(5);
        var task = MakeTask(10, 5);
        var rule = new MappingRule
        {
            Id = 1, Name = "R", MatchField = "ProjectKey",
            MatchOperator = MatchOperator.Equals, MatchValue = "X",
            Priority = 1, TimelogProjectId = 5, TimelogProject = project,
            TimelogTaskId = 10, TimelogTask = task, IsEnabled = true,
        };

        var result = MappingResult.Matched(rule, project, task);

        Assert.True(result.IsMatched);
        Assert.Equal(5, result.Project!.Id);
        Assert.Equal(10, result.Task!.Id);
        Assert.Same(rule, result.MatchedRule);
    }

    [Fact]
    public void MappingResult_Unmatched_HasNoProjectOrRule()
    {
        var result = MappingResult.Unmatched();
        Assert.False(result.IsMatched);
        Assert.Null(result.Project);
        Assert.Null(result.Task);
        Assert.Null(result.MatchedRule);
    }
}
