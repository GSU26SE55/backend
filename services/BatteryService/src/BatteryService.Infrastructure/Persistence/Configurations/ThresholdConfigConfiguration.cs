using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class ThresholdConfigConfiguration : IEntityTypeConfiguration<ThresholdConfig>
{
    public void Configure(EntityTypeBuilder<ThresholdConfig> builder)
    {
        builder.ToTable("threshold_configs");

        builder.HasKey(config => config.Id);

        builder.Property(config => config.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(config => config.BatteryTypeId)
            .HasColumnName("battery_type_id")
            .IsRequired();

        builder.Property(config => config.VoltageMin)
            .HasColumnName("voltage_min")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(config => config.VoltageMax)
            .HasColumnName("voltage_max")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(config => config.TemperatureMax)
            .HasColumnName("temperature_max")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(config => config.TemperatureMin)
            .HasColumnName("temperature_min")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(config => config.SocWarningThreshold)
            .HasColumnName("soc_warning_threshold")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(config => config.SocCriticalThreshold)
            .HasColumnName("soc_critical_threshold")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(config => config.CurrentMaxCharge)
            .HasColumnName("current_max_charge")
            .HasPrecision(8, 2);

        builder.Property(config => config.CurrentMaxDischarge)
            .HasColumnName("current_max_discharge")
            .HasPrecision(8, 2);

        builder.Property(config => config.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(config => config.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(config => config.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(config => config.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(config => config.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(config => config.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(config => config.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasOne(config => config.BatteryType)
            .WithMany(type => type.ThresholdConfigs)
            .HasForeignKey(config => config.BatteryTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(config => config.BatteryTypeId);
        builder.HasIndex(config => new { config.BatteryTypeId, config.IsActive })
            .IsUnique()
            .HasFilter("is_active = true AND is_deleted = false");

        builder.Ignore(config => config.DomainEvents);
    }
}
