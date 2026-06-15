using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotFirmwareUpdateLogConfiguration : IEntityTypeConfiguration<IotFirmwareUpdateLog>
{
    public void Configure(EntityTypeBuilder<IotFirmwareUpdateLog> builder)
    {
        builder.ToTable("iot_firmware_update_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.IotDeviceId).HasColumnName("iot_device_id").IsRequired();
        builder.Property(l => l.IotFirmwareReleaseId).HasColumnName("iot_firmware_release_id").IsRequired();

        builder.Property(l => l.FromVersion).HasColumnName("from_version").HasMaxLength(32);
        builder.Property(l => l.ToVersion).HasColumnName("to_version").HasMaxLength(32).IsRequired();

        builder.Property(l => l.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(IotFirmwareUpdateStatusEnum.Pending)
            .IsRequired();

        builder.Property(l => l.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(l => l.CompletedAt).HasColumnName("completed_at");
        builder.Property(l => l.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(l => l.BytesDownloaded).HasColumnName("bytes_downloaded");

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(l => l.IotDevice)
            .WithMany(d => d.FirmwareUpdateLogs)
            .HasForeignKey(l => l.IotDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.IotFirmwareRelease)
            .WithMany(r => r.UpdateLogs)
            .HasForeignKey(l => l.IotFirmwareReleaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.IotDeviceId, l.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_iot_firmware_log_device_time");

        builder.HasIndex(l => l.Status).HasDatabaseName("idx_iot_firmware_log_status");

        builder.Ignore(l => l.DomainEvents);
    }
}
