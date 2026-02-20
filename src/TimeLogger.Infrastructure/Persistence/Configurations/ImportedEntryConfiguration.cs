using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeLogger.Domain.Entities;

namespace TimeLogger.Infrastructure.Persistence.Configurations;

public class ImportedEntryConfiguration : IEntityTypeConfiguration<ImportedEntry>
{
    public void Configure(EntityTypeBuilder<ImportedEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UserEmail).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ProjectKey).HasMaxLength(100);
        builder.Property(x => x.IssueKey).HasMaxLength(100);
        builder.Property(x => x.Activity).HasMaxLength(200);
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => new { x.ImportSourceId, x.ExternalId }).IsUnique();

        builder.HasOne(x => x.ImportSource)
            .WithMany(x => x.ImportedEntries)
            .HasForeignKey(x => x.ImportSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.MappingRule)
            .WithMany(x => x.MatchedEntries)
            .HasForeignKey(x => x.MappingRuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.TimelogProject)
            .WithMany(x => x.ImportedEntries)
            .HasForeignKey(x => x.TimelogProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.TimelogTask)
            .WithMany(x => x.ImportedEntries)
            .HasForeignKey(x => x.TimelogTaskId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder.HasOne(x => x.SubmittedEntry)
            .WithOne(x => x.ImportedEntry)
            .HasForeignKey<SubmittedEntry>(x => x.ImportedEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
