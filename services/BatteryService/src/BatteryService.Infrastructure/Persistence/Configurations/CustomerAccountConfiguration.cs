using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(account => account.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(account => account.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(30);

        builder.Property(account => account.Role)
            .HasColumnName("role")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(account => account.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(account => account.LastSyncedAtUtc)
            .HasColumnName("last_synced_at_utc")
            .IsRequired();

        builder.Property(account => account.LastSourceEventAtUtc)
            .HasColumnName("last_source_event_at_utc");

        builder.Property(account => account.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(account => account.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(account => account.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(account => account.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(account => account.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(account => account.Email)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(account => new { account.IsActive, account.IsDeleted });

        builder.Ignore(account => account.DomainEvents);
    }
}
