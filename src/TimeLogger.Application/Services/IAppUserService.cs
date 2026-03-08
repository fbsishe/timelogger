using TimeLogger.Domain;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Application.Services;

public interface IAppUserService
{
    /// <summary>
    /// Creates user on first login or updates LastLoginAt on subsequent logins.
    /// Also attempts to auto-match to an EmployeeMapping.
    /// </summary>
    Task<AppUser> EnsureUserAsync(string oid, string email, string displayName, CancellationToken ct = default);

    Task<AppUser?> GetByOidAsync(string oid, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default);
    Task SetRoleAsync(int userId, AppRole role, CancellationToken ct = default);
    Task SetProjectsAsync(int userId, IReadOnlyList<int> projectIds, CancellationToken ct = default);
}
