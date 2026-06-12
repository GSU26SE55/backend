using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("sites");

        builder.HasKey(site => site.Id);

        builder.Property(site => site.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(site => site.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(site => site.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(site => site.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(site => site.Latitude)
            .HasColumnName("latitude")
            .HasPrecision(9, 6);

        builder.Property(site => site.Longitude)
            .HasColumnName("longitude")
            .HasPrecision(9, 6);

        builder.Property(site => site.InstallDate)
            .HasColumnName("install_date")
            .IsRequired();

        builder.Property(site => site.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(SiteStatusEnum.Active)
            .IsRequired();

        builder.Property(site => site.ContactPersonName)
            .HasColumnName("contact_person_name")
            .HasMaxLength(150);

        builder.Property(site => site.ContactPersonPhone)
            .HasColumnName("contact_person_phone")
            .HasMaxLength(30);

        builder.Property(site => site.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(site => site.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(site => site.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(site => site.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(site => site.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(site => site.CustomerId);
        builder.HasIndex(site => site.Status);
        builder.HasIndex(site => new { site.CustomerId, site.IsDeleted, site.Status });
        builder.HasIndex(site => new { site.CustomerId, site.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.Ignore(site => site.DomainEvents);
    }
}
