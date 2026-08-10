using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");

        builder.HasKey(alert => alert.Id);

        builder.Property(alert => alert.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // Sprint 5B #100 — battery_asset_id nullable cho site-level alert.
        builder.Property(alert => alert.BatteryAssetId)
            .HasColumnName("battery_asset_id");

        builder.Property(alert => alert.SiteId)
            .HasColumnName("site_id");

        builder.Property(alert => alert.EnvironmentalIncidentId)
            .HasColumnName("environmental_incident_id");

        builder.Property(alert => alert.AnomalyType)
            .HasColumnName("anomaly_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(alert => alert.Severity)
            .HasColumnName("severity")
            .HasConversion<int>()
            .IsRequired();

        // §1.3.5 — nullable cho incident-based alert (smoke/water không có threshold).
        builder.Property(alert => alert.ThresholdValue)
            .HasColumnName("threshold_value")
            .HasPrecision(10, 4);

        builder.Property(alert => alert.ActualValue)
            .HasColumnName("actual_value")
            .HasPrecision(10, 4);

        builder.Property(alert => alert.Unit)
            .HasColumnName("unit")
            .HasMaxLength(10);

        builder.Property(alert => alert.DetectedAt)
            .HasColumnName("detected_at")
            .IsRequired();

        builder.Property(alert => alert.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(AlertStatusEnum.Open)
            .IsRequired();

        builder.Property(alert => alert.MergedIntoAlertId)
            .HasColumnName("merged_into_alert_id");

        builder.Property(alert => alert.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(alert => alert.AcknowledgedByUserId)
            .HasColumnName("acknowledged_by_user_id");

        builder.Property(alert => alert.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(alert => alert.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(alert => alert.DedupWindowEndUtc)
            .HasColumnName("dedup_window_end_utc")
            .IsRequired();

        // GH-778 — id prescription của AI (uuid dạng chuỗi). 64 ký tự dư sức, và giới hạn độ dài
        // để một id dị thường không âm thầm phình cột.
        builder.Property(a => a.AiPrescriptionId)
            .HasColumnName("ai_prescription_id")
            .HasMaxLength(64);

        builder.Property(alert => alert.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(alert => alert.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(alert => alert.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(alert => alert.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(alert => alert.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasOne(alert => alert.BatteryAsset)
            .WithMany(asset => asset.Alerts)
            .HasForeignKey(alert => alert.BatteryAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(alert => alert.Site)
            .WithMany()
            .HasForeignKey(alert => alert.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        // EnvironmentalIncident navigation configured ở EnvironmentalIncidentConfiguration (HasMany side).

        builder.HasOne(alert => alert.MergedIntoAlert)
            .WithMany(alert => alert.MergedAlerts)
            .HasForeignKey(alert => alert.MergedIntoAlertId)
            .OnDelete(DeleteBehavior.Restrict);

        // Check constraint: ít nhất 1 trong BatteryAssetId / SiteId không null.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_alerts_asset_or_site_required",
            "battery_asset_id IS NOT NULL OR site_id IS NOT NULL"));

        builder.HasIndex(alert => alert.BatteryAssetId);
        builder.HasIndex(alert => alert.SiteId);
        builder.HasIndex(alert => alert.EnvironmentalIncidentId);
        builder.HasIndex(alert => alert.Status);
        builder.HasIndex(alert => alert.Severity);
        builder.HasIndex(alert => new { alert.BatteryAssetId, alert.AnomalyType, alert.Status, alert.DedupWindowEndUtc });

        builder.Ignore(alert => alert.DomainEvents);
    }
}
