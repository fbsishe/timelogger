using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class TimelogTaskConfiguration : IEntityTypeConfiguration<TimelogTask>
{
    public void Configure(EntityTypeBuilder<TimelogTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TimelogProjectId, x.ExternalId }).IsUnique();

        builder.HasOne(x => x.TimelogProject)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => x.TimelogProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
