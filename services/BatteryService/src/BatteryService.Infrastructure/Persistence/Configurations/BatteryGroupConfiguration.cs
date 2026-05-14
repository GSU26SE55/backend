using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class BatteryGroupConfiguration : IEntityTypeConfiguration<BatteryGroup>
{
    public void Configure(EntityTypeBuilder<BatteryGroup> builder)
    {
        builder.ToTable("battery_groups");

        builder.HasKey(group => group.Id);

        builder.Property(group => group.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(group => group.SiteId)
            .HasColumnName("site_id")
            .IsRequired();

        builder.Property(group => group.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(group => group.BatteryTypeId)
            .HasColumnName("battery_type_id")
            .IsRequired();

        builder.Property(group => group.BatteryCount)
            .HasColumnName("battery_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(group => group.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(group => group.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(group => group.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(group => group.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(group => group.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasOne(group => group.Site)
            .WithMany(site => site.BatteryGroups)
            .HasForeignKey(group => group.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(group => group.BatteryType)
            .WithMany(type => type.BatteryGroups)
            .HasForeignKey(group => group.BatteryTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(group => group.SiteId);
        builder.HasIndex(group => group.BatteryTypeId);
        builder.HasIndex(group => new { group.SiteId, group.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.Ignore(group => group.DomainEvents);
    }
}
