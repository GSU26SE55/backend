using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class AmbientReadingConfiguration : IEntityTypeConfiguration<AmbientReading>
{
    public void Configure(EntityTypeBuilder<AmbientReading> builder)
    {
        builder.ToTable("ambient_readings");

        // Composite PK: (time, site_id) — TimescaleDB hypertable requirement.
        builder.HasKey(a => new { a.Time, a.SiteId });

        builder.Property(a => a.Time)
            .HasColumnName("time")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(a => a.SiteId)
            .HasColumnName("site_id")
            .IsRequired();

        builder.Property(a => a.AmbientTemperature)
            .HasColumnName("ambient_temperature_celsius")
            .HasPrecision(6, 2);

        builder.Property(a => a.Humidity)
            .HasColumnName("humidity_percent")
            .HasPrecision(5, 2);

        builder.Property(a => a.SolarIrradiance)
            .HasColumnName("solar_irradiance_wm2")
            .HasPrecision(8, 2);

        builder.Property(a => a.GasConcentration)
            .HasColumnName("gas_concentration_percent")
            .HasPrecision(5, 2);

        builder.Property(a => a.WaterLeakDetected)
            .HasColumnName("water_leak_detected");

        builder.Property(a => a.Source)
            .HasColumnName("source")
            .HasConversion<int>()
            .HasDefaultValue(BatteryService.Domain.Enums.AmbientReadingSourceEnum.WeatherApi)
            .IsRequired();

        builder.Property(a => a.SourceDeviceId)
            .HasColumnName("source_device_id")
            .HasMaxLength(64);

        builder.HasOne(a => a.Site)
            .WithMany()
            .HasForeignKey(a => a.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.SiteId, a.Time })
            .HasDatabaseName("ix_ambient_readings_site_time");
    }
}
