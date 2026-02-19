using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Mapping;

/// <summary>The outcome of evaluating mapping rules against a single <see cref="ImportedEntry"/>.</summary>
public sealed class MappingResult
{
    private MappingResult() { }

    public bool IsMatched { get; private init; }
    public MappingRule? MatchedRule { get; private init; }
    public TimelogProject? Project { get; private init; }
    public TimelogTask? Task { get; private init; }

    public static MappingResult Matched(MappingRule rule, TimelogProject project, TimelogTask? task) =>
        new() { IsMatched = true, MatchedRule = rule, Project = project, Task = task };

    public static MappingResult Unmatched() =>
        new() { IsMatched = false };
}
