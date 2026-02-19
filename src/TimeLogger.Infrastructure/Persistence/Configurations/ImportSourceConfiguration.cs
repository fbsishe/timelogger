using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class ImportSourceConfiguration : IEntityTypeConfiguration<ImportSource>
{
    public void Configure(EntityTypeBuilder<ImportSource> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ApiToken).HasMaxLength(500);
        builder.Property(x => x.BaseUrl).HasMaxLength(500);
        builder.Property(x => x.PollSchedule).HasMaxLength(100);
    }
}
