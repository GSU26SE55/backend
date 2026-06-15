using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotDeviceCalibrationConfiguration : IEntityTypeConfiguration<IotDeviceCalibration>
{
    public void Configure(EntityTypeBuilder<IotDeviceCalibration> builder)
    {
        builder.ToTable("iot_device_calibrations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.IotDeviceId).HasColumnName("iot_device_id").IsRequired();

        builder.Property(c => c.Channel)
            .HasColumnName("channel")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.BatteryAssetId).HasColumnName("battery_asset_id");

        builder.Property(c => c.Scale)
            .HasColumnName("scale")
            .HasPrecision(12, 6)
            .HasDefaultValue(1m)
            .IsRequired();

        builder.Property(c => c.Offset)
            .HasColumnName("offset_value")
            .HasPrecision(12, 6)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(c => c.Unit)
            .HasColumnName("unit")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.CalibratedAt).HasColumnName("calibrated_at").IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(c => c.IotDevice)
            .WithMany(d => d.Calibrations)
            .HasForeignKey(c => c.IotDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.IotDeviceId, c.Channel, c.BatteryAssetId })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("idx_iot_calibrations_lookup");

        builder.Ignore(c => c.DomainEvents);
    }
}
