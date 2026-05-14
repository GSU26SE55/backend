using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class BatteryTypeConfiguration : IEntityTypeConfiguration<BatteryType>
{
    public void Configure(EntityTypeBuilder<BatteryType> builder)
    {
        builder.ToTable("battery_types");

        builder.HasKey(type => type.Id);

        builder.Property(type => type.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(type => type.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(type => type.Manufacturer)
            .HasColumnName("manufacturer")
            .HasMaxLength(100);

        builder.Property(type => type.NominalCapacityAh)
            .HasColumnName("nominal_capacity_ah")
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(type => type.NominalVoltage)
            .HasColumnName("nominal_voltage")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(type => type.Chemistry)
            .HasColumnName("chemistry")
            .HasConversion<int>()
            .HasDefaultValue(BatteryChemistryEnum.LiFePO4)
            .IsRequired();

        builder.Property(type => type.MaxCycleCount)
            .HasColumnName("max_cycle_count")
            .HasDefaultValue(2000)
            .IsRequired();

        builder.Property(type => type.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(type => type.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(type => type.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(type => type.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(type => type.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(type => type.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(type => type.Name)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(type => type.Chemistry);
        builder.HasIndex(type => type.IsDeleted);

        builder.Ignore(type => type.DomainEvents);
    }
}
