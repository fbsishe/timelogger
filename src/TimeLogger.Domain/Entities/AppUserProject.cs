namespace TimeLogger.Domain.Entities;

public class AppUserProject
{
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public int TimelogProjectId { get; set; }
    public TimelogProject TimelogProject { get; set; } = null!;
}
