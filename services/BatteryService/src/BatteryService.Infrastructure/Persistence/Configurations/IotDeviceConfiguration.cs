using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotDeviceConfiguration : IEntityTypeConfiguration<IotDevice>
{
    public void Configure(EntityTypeBuilder<IotDevice> builder)
    {
        builder.ToTable("iot_devices");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.DeviceCode)
            .HasColumnName("device_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(d => d.SiteId).HasColumnName("site_id").IsRequired();

        builder.Property(d => d.HardwareRevision)
            .HasColumnName("hardware_revision")
            .HasMaxLength(64);

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(IotDeviceStatusEnum.Pending)
            .IsRequired();

        builder.Property(d => d.CurrentFirmwareVersion)
            .HasColumnName("current_firmware_version")
            .HasMaxLength(32);

        builder.Property(d => d.TargetFirmwareReleaseId).HasColumnName("target_firmware_release_id");

        builder.Property(d => d.ApiKeyHash)
            .HasColumnName("api_key_hash")
            .HasMaxLength(128)
            .IsRequired();

        // Plaintext key để Admin xem lại trên GET by id — nullable (device cũ chưa có).
        builder.Property(d => d.ApiKeyPlaintext)
            .HasColumnName("api_key_plaintext")
            .HasMaxLength(128);

        builder.Property(d => d.ApiKeyLastFour)
            .HasColumnName("api_key_last_four")
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(d => d.ApiKeyScopes)
            .HasColumnName("api_key_scopes")
            .HasConversion<int>()
            .HasDefaultValue(IotApiKeyScopeEnum.EdgeDeviceDefault)
            .IsRequired();

        builder.Property(d => d.ApiKeyIssuedAt).HasColumnName("api_key_issued_at").IsRequired();
        builder.Property(d => d.ApiKeyRevokedAt).HasColumnName("api_key_revoked_at");

        builder.Property(d => d.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(d => d.LastProvisionedAt).HasColumnName("last_provisioned_at");
        builder.Property(d => d.LastOfflineAt).HasColumnName("last_offline_at");

        builder.Property(d => d.HeartbeatIntervalSeconds)
            .HasColumnName("heartbeat_interval_seconds")
            .HasDefaultValue(60)
            .IsRequired();

        builder.Property(d => d.LastClockSkewSeconds).HasColumnName("last_clock_skew_seconds");

        // Sprint IoT-2 #IoT2-17 — auto-disable outlier tracking.
        builder.Property(d => d.OutlierIncidentCount).HasColumnName("outlier_incident_count").HasDefaultValue(0);
        builder.Property(d => d.OutlierWindowStartedAt).HasColumnName("outlier_window_started_at");
        builder.Property(d => d.AutoDecommissionedAt).HasColumnName("auto_decommissioned_at");

        // Sprint IoT-2 #IoT2-26 — MQTT per-device credential.
        builder.Property(d => d.MqttUsername).HasColumnName("mqtt_username").HasMaxLength(128);
        builder.Property(d => d.MqttPasswordHash).HasColumnName("mqtt_password_hash").HasMaxLength(256);

        builder.Property(d => d.Notes).HasColumnName("notes").HasMaxLength(1000);

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(d => d.Site)
            .WithMany()
            .HasForeignKey(d => d.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.TargetFirmwareRelease)
            .WithMany()
            .HasForeignKey(d => d.TargetFirmwareReleaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.DeviceCode)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("idx_iot_devices_device_code");

        builder.HasIndex(d => d.ApiKeyHash)
            .HasDatabaseName("idx_iot_devices_api_key_hash");

        builder.HasIndex(d => d.SiteId).HasDatabaseName("idx_iot_devices_site");
        builder.HasIndex(d => new { d.Status, d.LastSeenAt }).HasDatabaseName("idx_iot_devices_status_last_seen");

        builder.Ignore(d => d.DomainEvents);
    }
}
