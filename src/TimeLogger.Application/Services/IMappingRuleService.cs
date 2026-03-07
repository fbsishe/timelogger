using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public record MappingRuleConditionDto(string MatchField, MatchOperator MatchOperator, string MatchValue);

public record MappingRuleDto(
    int Id,
    string Name,
    SourceType? SourceType,
    IReadOnlyList<MappingRuleConditionDto> Conditions,
    int TimelogProjectId,
    string TimelogProjectName,
    int? TimelogTaskId,
    string? TimelogTaskName,
    int Priority,
    bool IsEnabled,
    bool IncludeIssueKeyInComment);

public record CreateMappingRuleRequest(
    string Name,
    SourceType? SourceType,
    IReadOnlyList<MappingRuleConditionDto> Conditions,
    int TimelogProjectId,
    int? TimelogTaskId,
    int Priority,
    bool IncludeIssueKeyInComment);

public interface IMappingRuleService
{
    Task<IReadOnlyList<MappingRuleDto>> GetAllAsync(CancellationToken ct = default);
    Task<MappingRule> CreateAsync(CreateMappingRuleRequest request, CancellationToken ct = default);
    Task UpdateAsync(int id, CreateMappingRuleRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);
    Task MovePriorityAsync(int id, int direction, CancellationToken ct = default);
    Task<IReadOnlyList<EntryListItem>> TestRuleAsync(int id, CancellationToken ct = default);
}
