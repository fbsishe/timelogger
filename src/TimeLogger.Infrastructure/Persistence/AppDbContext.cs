using Microsoft.EntityFrameworkCore;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ImportSource> ImportSources => Set<ImportSource>();
    public DbSet<ImportedEntry> ImportedEntries => Set<ImportedEntry>();
    public DbSet<MappingRule> MappingRules => Set<MappingRule>();
    public DbSet<TimelogProject> TimelogProjects => Set<TimelogProject>();
    public DbSet<TimelogTask> TimelogTasks => Set<TimelogTask>();
    public DbSet<SubmittedEntry> SubmittedEntries => Set<SubmittedEntry>();
    public DbSet<EmployeeMapping> EmployeeMappings => Set<EmployeeMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
