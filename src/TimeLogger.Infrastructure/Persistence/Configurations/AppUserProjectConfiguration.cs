using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class AppUserProjectConfiguration : IEntityTypeConfiguration<AppUserProject>
{
    public void Configure(EntityTypeBuilder<AppUserProject> builder)
    {
        builder.HasKey(up => new { up.AppUserId, up.TimelogProjectId });

        builder.HasOne(up => up.AppUser)
            .WithMany(u => u.AssignedProjects)
            .HasForeignKey(up => up.AppUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(up => up.TimelogProject)
            .WithMany()
            .HasForeignKey(up => up.TimelogProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
