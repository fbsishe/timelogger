using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class MappingRuleConditionConfiguration : IEntityTypeConfiguration<MappingRuleCondition>
{
    public void Configure(EntityTypeBuilder<MappingRuleCondition> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MatchField).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MatchValue).HasMaxLength(500).IsRequired();
    }
}
