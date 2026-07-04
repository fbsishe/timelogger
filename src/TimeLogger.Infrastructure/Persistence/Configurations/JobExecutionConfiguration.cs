using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.JobName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.HasIndex(x => new { x.JobName, x.ExecutedAt });
    }
}
