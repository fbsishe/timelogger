namespace TimeLogger.Domain.Entities;

public class AppUser
{
    public int Id { get; set; }
    public required string EntraObjectId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public AppRole Role { get; set; } = AppRole.User;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastLoginAt { get; set; } = DateTimeOffset.UtcNow;
    public int? EmployeeMappingId { get; set; }
    public EmployeeMapping? EmployeeMapping { get; set; }
    public ICollection<AppUserProject> AssignedProjects { get; set; } = [];
}
