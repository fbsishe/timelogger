using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class SubmittedEntryConfiguration : IEntityTypeConfiguration<SubmittedEntry>
{
    public void Configure(EntityTypeBuilder<SubmittedEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(200);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
    }
}
