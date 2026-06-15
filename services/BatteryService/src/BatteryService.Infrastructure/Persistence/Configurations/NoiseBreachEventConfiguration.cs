using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class NoiseBreachEventConfiguration : IEntityTypeConfiguration<NoiseBreachEvent>
{
    public void Configure(EntityTypeBuilder<NoiseBreachEvent> builder)
    {
        builder.ToTable("noise_breach_events");

        // Composite PK (time, battery_asset_id, anomaly_type) — hypertable requirement.
        builder.HasKey(n => new { n.Time, n.BatteryAssetId, n.AnomalyType });

        builder.Property(n => n.Time)
            .HasColumnName("time")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(n => n.BatteryAssetId)
            .HasColumnName("battery_asset_id")
            .IsRequired();

        builder.Property(n => n.AnomalyType)
            .HasColumnName("anomaly_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(n => n.ThresholdValue).HasColumnName("threshold_value").HasPrecision(18, 6);
        builder.Property(n => n.ActualValue).HasColumnName("actual_value").HasPrecision(18, 6);
        builder.Property(n => n.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();

        // Sprint 5B audit — link tới alert nếu breach được promote thật.
        builder.Property(n => n.PromotedToAlertId).HasColumnName("promoted_to_alert_id");

        // Sprint 5B B9 — phân biệt nguồn breach (BMS/IoT).
        builder.Property(n => n.SourceType)
            .HasColumnName("source_type")
            .HasConversion<int>()
            .HasDefaultValue(BatteryService.Domain.Enums.SensorReadingSourceTypeEnum.IotGateway)
            .IsRequired();

        builder.HasOne(n => n.BatteryAsset)
            .WithMany()
            .HasForeignKey(n => n.BatteryAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.BatteryAssetId, n.AnomalyType, n.Time })
            .HasDatabaseName("ix_noise_breach_events_asset_type_time");

        // Index cho query "breach đã promote thành alert" — audit + retention.
        builder.HasIndex(n => n.PromotedToAlertId)
            .HasFilter("promoted_to_alert_id IS NOT NULL")
            .HasDatabaseName("ix_noise_breach_events_promoted_alert");
    }
}
