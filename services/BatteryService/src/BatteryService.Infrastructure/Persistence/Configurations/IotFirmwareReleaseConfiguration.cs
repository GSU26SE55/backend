using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotFirmwareReleaseConfiguration : IEntityTypeConfiguration<IotFirmwareRelease>
{
    public void Configure(EntityTypeBuilder<IotFirmwareRelease> builder)
    {
        builder.ToTable("iot_firmware_releases");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.Version)
            .HasColumnName("version")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.HardwareRevision)
            .HasColumnName("hardware_revision")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ArtifactUrl)
            .HasColumnName("artifact_url")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.Sha256Checksum)
            .HasColumnName("sha256_checksum")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ArtifactSizeBytes)
            .HasColumnName("artifact_size_bytes")
            .IsRequired();

        builder.Property(r => r.ReleaseNotes)
            .HasColumnName("release_notes");

        builder.Property(r => r.IsPublished)
            .HasColumnName("is_published")
            .HasDefaultValue(false);

        builder.Property(r => r.PublishedAt).HasColumnName("published_at");

        builder.Property(r => r.IsArchived)
            .HasColumnName("is_archived")
            .HasDefaultValue(false);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(r => new { r.Version, r.HardwareRevision })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("idx_iot_firmware_version_hw");

        builder.HasIndex(r => new { r.HardwareRevision, r.IsPublished, r.IsArchived })
            .HasDatabaseName("idx_iot_firmware_rollout");

        builder.Ignore(r => r.DomainEvents);
    }
}
