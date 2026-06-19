using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

/// <summary>
/// #AUTH-48: EF Config cho <see cref="TrustedDevice"/>.
/// </summary>
public class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("trusted_devices");

        builder.HasKey(td => td.Id);

        builder.Property(td => td.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(td => td.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(td => td.DeviceFingerprintHash)
            .HasColumnName("device_fingerprint_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(td => td.IpPrefix)
            .HasColumnName("ip_prefix")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(td => td.Label)
            .HasColumnName("label")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(td => td.UserAgentSnapshot)
            .HasColumnName("user_agent_snapshot")
            .HasMaxLength(512);

        builder.Property(td => td.TrustedAt)
            .HasColumnName("trusted_at")
            .IsRequired();

        builder.Property(td => td.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(td => td.LastUsedAt)
            .HasColumnName("last_used_at");

        builder.Property(td => td.UsageCount)
            .HasColumnName("usage_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(td => td.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(td => td.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasMaxLength(255);

        // AuditableEntity fields
        builder.Property(td => td.CreatedAt).HasColumnName("created_at");
        builder.Property(td => td.CreatedBy).HasColumnName("created_by");
        builder.Property(td => td.UpdatedAt).HasColumnName("updated_at");
        builder.Property(td => td.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(td => td.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(td => td.Account)
            .WithMany()
            .HasForeignKey(td => td.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // Composite unique để 1 device fingerprint chỉ 1 row active per account (filter is_deleted + revoked).
        builder.HasIndex(td => new { td.AccountId, td.DeviceFingerprintHash })
            .HasFilter("\"is_deleted\" = false AND \"revoked_at\" IS NULL")
            .HasDatabaseName("ix_trusted_devices_account_fingerprint_active")
            .IsUnique();

        // Index cho query "list my trusted devices" (filter active by expiry).
        builder.HasIndex(td => new { td.AccountId, td.ExpiresAt })
            .HasDatabaseName("ix_trusted_devices_account_expires");

        builder.HasQueryFilter(td => !td.IsDeleted);
    }
}
