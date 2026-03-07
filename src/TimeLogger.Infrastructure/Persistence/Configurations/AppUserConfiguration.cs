using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.EntraObjectId).IsUnique();
        builder.Property(u => u.EntraObjectId).HasMaxLength(36).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Role).HasConversion<int>();

        builder.HasOne(u => u.EmployeeMapping)
            .WithMany()
            .HasForeignKey(u => u.EmployeeMappingId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
