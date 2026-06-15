using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotDeviceHeartbeatConfiguration : IEntityTypeConfiguration<IotDeviceHeartbeat>
{
    public void Configure(EntityTypeBuilder<IotDeviceHeartbeat> builder)
    {
        builder.ToTable("iot_device_heartbeats");

        builder.HasKey(h => new { h.Time, h.IotDeviceId });

        builder.Property(h => h.Time).HasColumnName("time").IsRequired();
        builder.Property(h => h.IotDeviceId).HasColumnName("iot_device_id").IsRequired();

        builder.Property(h => h.FirmwareVersion)
            .HasColumnName("firmware_version")
            .HasMaxLength(32);

        builder.Property(h => h.RssiDbm).HasColumnName("rssi_dbm");
        builder.Property(h => h.FreeMemoryPercent).HasColumnName("free_memory_percent").HasPrecision(5, 2);
        builder.Property(h => h.UptimeSeconds).HasColumnName("uptime_seconds");
        builder.Property(h => h.QueuedReadingCount).HasColumnName("queued_reading_count");
        builder.Property(h => h.DeviceTimestamp).HasColumnName("device_timestamp");
        builder.Property(h => h.ClockSkewSeconds).HasColumnName("clock_skew_seconds");

        builder.HasOne(h => h.IotDevice)
            .WithMany(d => d.Heartbeats)
            .HasForeignKey(h => h.IotDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.IotDeviceId, h.Time })
            .IsDescending(false, true)
            .HasDatabaseName("idx_iot_heartbeats_device_time");
    }
}
