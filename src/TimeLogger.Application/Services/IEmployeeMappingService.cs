namespace TimeLogger.Application.Services;

public record EmployeeMappingDto(
    int Id,
    string AtlassianAccountId,
    string? DisplayName,
    int TimelogUserId,
    string? TimelogUserDisplayName,
    DateTimeOffset UpdatedAt);

public record KnownAccountIdDto(string AccountId, string? DisplayName);

public record TimelogUserSummaryDto(int UserId, string FullName, string? Email);

public record UpsertEmployeeMappingRequest(
    string AtlassianAccountId,
    string? DisplayName,
    int TimelogUserId,
    string? TimelogUserDisplayName);

public interface IEmployeeMappingService
{
    Task<IReadOnlyList<EmployeeMappingDto>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(UpsertEmployeeMappingRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<KnownAccountIdDto>> GetUnmappedAccountIdsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TimelogUserSummaryDto>> GetTimelogUsersAsync(CancellationToken ct = default);
    Task<string?> FetchJiraDisplayNameAsync(string accountId, CancellationToken ct = default);
}
