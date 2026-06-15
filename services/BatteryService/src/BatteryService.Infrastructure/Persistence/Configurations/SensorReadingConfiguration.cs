using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class SensorReadingConfiguration : IEntityTypeConfiguration<SensorReading>
{
    public void Configure(EntityTypeBuilder<SensorReading> builder)
    {
        builder.ToTable("sensor_readings");

        builder.HasKey(reading => new { reading.Time, reading.BatteryAssetId });

        builder.Property(reading => reading.Time)
            .HasColumnName("time")
            .IsRequired();

        builder.Property(reading => reading.BatteryAssetId)
            .HasColumnName("battery_asset_id")
            .IsRequired();

        builder.Property(reading => reading.Voltage)
            .HasColumnName("voltage")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(reading => reading.Current)
            .HasColumnName("current")
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(reading => reading.Temperature)
            .HasColumnName("temperature")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(reading => reading.SocPercent)
            .HasColumnName("soc_percent")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(reading => reading.CycleCount)
            .HasColumnName("cycle_count");

        builder.Property(reading => reading.SohPercent)
            .HasColumnName("soh_percent")
            .HasPrecision(5, 2);

        builder.Property(reading => reading.ChargingState)
            .HasColumnName("charging_state")
            .HasConversion<int?>();

        builder.Property(reading => reading.SourceDeviceId)
            .HasColumnName("source_device_id")
            .HasMaxLength(64);

        // Sprint 5B #101 — Tier 2 battery health metrics.
        builder.Property(reading => reading.InternalResistanceMilliohm)
            .HasColumnName("internal_resistance_milliohm")
            .HasPrecision(10, 3);

        builder.Property(reading => reading.CellVoltageDeltaMv)
            .HasColumnName("cell_voltage_delta_mv")
            .HasPrecision(10, 3);

        // Sprint 5B B9 — phân biệt nguồn đo.
        builder.Property(reading => reading.SourceType)
            .HasColumnName("source_type")
            .HasConversion<int>()
            .HasDefaultValue(BatteryService.Domain.Enums.SensorReadingSourceTypeEnum.IotGateway)
            .IsRequired();

        builder.Property(reading => reading.BmsErrorCode)
            .HasColumnName("bms_error_code")
            .HasMaxLength(64);

        builder.Property(reading => reading.SensorSourceCode)
            .HasColumnName("sensor_source_code")
            .HasMaxLength(20);

        builder.HasOne(reading => reading.BatteryAsset)
            .WithMany(asset => asset.SensorReadings)
            .HasForeignKey(reading => reading.BatteryAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reading => new { reading.BatteryAssetId, reading.Time })
            .IsDescending(false, true)
            .HasDatabaseName("idx_sensor_readings_asset_time");
    }
}
