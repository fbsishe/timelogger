using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class TimelogProjectConfiguration : IEntityTypeConfiguration<TimelogProject>
{
    public void Configure(EntityTypeBuilder<TimelogProject> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => x.ExternalId).IsUnique();
    }
}
