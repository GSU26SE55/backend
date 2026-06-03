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

        builder.Property(alert => alert.BatteryAssetId)
            .HasColumnName("battery_asset_id")
            .IsRequired();

        builder.Property(alert => alert.AnomalyType)
            .HasColumnName("anomaly_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(alert => alert.Severity)
            .HasColumnName("severity")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(alert => alert.ThresholdValue)
            .HasColumnName("threshold_value")
            .HasPrecision(10, 4)
            .IsRequired();

        builder.Property(alert => alert.ActualValue)
            .HasColumnName("actual_value")
            .HasPrecision(10, 4)
            .IsRequired();

        builder.Property(alert => alert.Unit)
            .HasColumnName("unit")
            .HasMaxLength(10)
            .IsRequired();

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

        builder.HasOne(alert => alert.MergedIntoAlert)
            .WithMany(alert => alert.MergedAlerts)
            .HasForeignKey(alert => alert.MergedIntoAlertId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(alert => alert.BatteryAssetId);
        builder.HasIndex(alert => alert.Status);
        builder.HasIndex(alert => alert.Severity);
        builder.HasIndex(alert => new { alert.BatteryAssetId, alert.AnomalyType, alert.Status, alert.DedupWindowEndUtc });

        builder.Ignore(alert => alert.DomainEvents);
    }
}
