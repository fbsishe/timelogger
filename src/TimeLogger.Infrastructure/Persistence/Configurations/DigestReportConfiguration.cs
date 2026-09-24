using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class DigestReportConfiguration : IEntityTypeConfiguration<DigestReport>
{
    public void Configure(EntityTypeBuilder<DigestReport> builder)
    {
        builder.HasKey(x => x.Id);
        // Unique so two overlapping runs cannot both record the same day as covered.
        builder.HasIndex(x => x.ToDay).IsUnique();
    }
}
