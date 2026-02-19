using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class MappingRuleConfiguration : IEntityTypeConfiguration<MappingRule>
{
    public void Configure(EntityTypeBuilder<MappingRule> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MatchField).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MatchValue).HasMaxLength(500).IsRequired();

        builder.HasOne(x => x.TimelogProject)
            .WithMany(x => x.MappingRules)
            .HasForeignKey(x => x.TimelogProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TimelogTask)
            .WithMany(x => x.MappingRules)
            .HasForeignKey(x => x.TimelogTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
