using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class AmbientThresholdConfigConfiguration : IEntityTypeConfiguration<AmbientThresholdConfig>
{
    public void Configure(EntityTypeBuilder<AmbientThresholdConfig> builder)
    {
        builder.ToTable("ambient_threshold_configs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(c => c.SiteId).HasColumnName("site_id").IsRequired();

        builder.Property(c => c.HighAmbientTempWarning).HasColumnName("high_ambient_temp_warning").HasPrecision(6, 2);
        builder.Property(c => c.HighAmbientTempCritical).HasColumnName("high_ambient_temp_critical").HasPrecision(6, 2);

        builder.Property(c => c.HighHumidityWarning).HasColumnName("high_humidity_warning").HasPrecision(5, 2);
        builder.Property(c => c.HighHumidityCritical).HasColumnName("high_humidity_critical").HasPrecision(5, 2);

        builder.Property(c => c.HighGasWarning).HasColumnName("high_gas_warning").HasPrecision(5, 2);
        builder.Property(c => c.HighGasCritical).HasColumnName("high_gas_critical").HasPrecision(5, 2);

        builder.Property(c => c.ComboTempThreshold).HasColumnName("combo_temp_threshold").HasPrecision(6, 2);
        builder.Property(c => c.ComboHumidityThreshold).HasColumnName("combo_humidity_threshold").HasPrecision(5, 2);

        builder.Property(c => c.Enabled).HasColumnName("enabled").HasDefaultValue(true);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(c => c.Site)
            .WithMany()
            .HasForeignKey(c => c.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1 active config per site (filtered unique).
        builder.HasIndex(c => c.SiteId)
            .IsUnique()
            .HasFilter("is_deleted = false AND enabled = true")
            .HasDatabaseName("ux_ambient_threshold_configs_site_active");

        builder.Ignore(c => c.DomainEvents);
    }
}
