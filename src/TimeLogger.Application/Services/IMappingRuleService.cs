using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public record MappingRuleDto(
    int Id,
    string Name,
    SourceType? SourceType,
    string MatchField,
    MatchOperator MatchOperator,
    string MatchValue,
    int TimelogProjectId,
    string TimelogProjectName,
    int? TimelogTaskId,
    string? TimelogTaskName,
    int Priority,
    bool IsEnabled);

public record CreateMappingRuleRequest(
    string Name,
    SourceType? SourceType,
    string MatchField,
    MatchOperator MatchOperator,
    string MatchValue,
    int TimelogProjectId,
    int? TimelogTaskId,
    int Priority);

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
