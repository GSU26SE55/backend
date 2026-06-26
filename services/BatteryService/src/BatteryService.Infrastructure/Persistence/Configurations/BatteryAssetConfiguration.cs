using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class BatteryAssetConfiguration : IEntityTypeConfiguration<BatteryAsset>
{
    public void Configure(EntityTypeBuilder<BatteryAsset> builder)
    {
        builder.ToTable("battery_assets");

        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(asset => asset.SerialNumber)
            .HasColumnName("serial_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(asset => asset.BatteryTypeId)
            .HasColumnName("battery_type_id")
            .IsRequired();

        builder.Property(asset => asset.SiteId)
            .HasColumnName("site_id");

        builder.Property(asset => asset.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(asset => asset.InstallDate)
            .HasColumnName("install_date")
            .IsRequired();

        builder.Property(asset => asset.WarrantyEndDate)
            .HasColumnName("warranty_end_date");

        builder.Property(asset => asset.WarrantyStatus)
            .HasColumnName("warranty_status")
            .HasConversion<int>()
            .HasDefaultValue(WarrantyStatusEnum.Active)
            .IsRequired();

        builder.Property(asset => asset.Location)
            .HasColumnName("location")
            .HasMaxLength(255);

        builder.Property(asset => asset.Latitude)
            .HasColumnName("latitude")
            .HasPrecision(9, 6);

        builder.Property(asset => asset.Longitude)
            .HasColumnName("longitude")
            .HasPrecision(9, 6);

        builder.Property(asset => asset.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(BatteryStatusEnum.Active)
            .IsRequired();

        builder.Property(asset => asset.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(asset => asset.LastSensorReadingAt)
            .HasColumnName("last_sensor_reading_at");

        // Sprint 7 B4 (§31.7) — cascade risk fields.
        builder.Property(asset => asset.CascadeRiskScore)
            .HasColumnName("cascade_risk_score")
            .HasPrecision(4, 3)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(asset => asset.CascadeRiskUpdatedAt)
            .HasColumnName("cascade_risk_updated_at");

        builder.Property(asset => asset.ElectricalTopology)
            .HasColumnName("electrical_topology")
            .HasConversion<int>()
            .HasDefaultValue(ElectricalTopologyEnum.Independent)
            .IsRequired();

        builder.Property(asset => asset.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(asset => asset.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(asset => asset.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(asset => asset.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(asset => asset.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasOne(asset => asset.BatteryType)
            .WithMany(type => type.Assets)
            .HasForeignKey(asset => asset.BatteryTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(asset => asset.Site)
            .WithMany(site => site.BatteryAssets)
            .HasForeignKey(asset => asset.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(asset => asset.SerialNumber)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(asset => asset.CustomerId);
        builder.HasIndex(asset => asset.BatteryTypeId);
        builder.HasIndex(asset => asset.SiteId);
        builder.HasIndex(asset => asset.Status);
        builder.HasIndex(asset => asset.LastSensorReadingAt);
        builder.HasIndex(asset => new { asset.CustomerId, asset.IsDeleted, asset.Status });
        builder.HasIndex(asset => new { asset.SiteId, asset.IsDeleted, asset.Status });

        builder.Ignore(asset => asset.DomainEvents);
    }
}
