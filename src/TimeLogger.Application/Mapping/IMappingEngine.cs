using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Mapping;

public interface IMappingEngine
{
    /// <summary>
    /// Evaluates <paramref name="rules"/> in ascending priority order against <paramref name="entry"/>.
    /// Returns the first match, or <see cref="MappingResult.Unmatched"/> if no rule matches.
    /// </summary>
    /// <param name="rules">Rules to evaluate. Must have <c>TimelogProject</c> (and optionally <c>TimelogTask</c>) loaded.</param>
    MappingResult Evaluate(IEnumerable<MappingRule> rules, ImportedEntry entry);

    /// <summary>
    /// Returns true if <paramref name="rule"/> matches <paramref name="entry"/>, ignoring priority and enabled flag.
    /// Used for 'Test Rule' previews.
    /// </summary>
    bool Matches(MappingRule rule, ImportedEntry entry);

    /// <summary>
    /// Picks the task a matched rule assigns to <paramref name="entry"/>: the rule's
    /// overtime task when the entry carries the overtime work attribute, otherwise the regular task.
    /// </summary>
    TimelogTask? SelectTask(MappingRule rule, ImportedEntry entry);
}
