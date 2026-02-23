using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class EmployeeMappingConfiguration : IEntityTypeConfiguration<EmployeeMapping>
{
    public void Configure(EntityTypeBuilder<EmployeeMapping> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AtlassianAccountId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(300);
        builder.Property(x => x.TimelogUserDisplayName).HasMaxLength(300);
        builder.HasIndex(x => x.AtlassianAccountId).IsUnique();
    }
}
