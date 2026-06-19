using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(rt => rt.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(rt => rt.Token)
            .HasColumnName("token")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(rt => rt.JwtId)
            .HasColumnName("jwt_id")
            .HasMaxLength(128);

        builder.Property(rt => rt.IssuedAt)
            .HasColumnName("issued_at")
            .IsRequired();

        // #AUTH-28: thời điểm gốc của chain rotation. Nullable cho row cũ trước migration.
        builder.Property(rt => rt.OriginalIssuedAt)
            .HasColumnName("original_issued_at");

        builder.Property(rt => rt.ExpiredAt)
            .HasColumnName("expired_at")
            .IsRequired();

        builder.Property(rt => rt.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(RefreshTokenStatus.Active)
            .IsRequired();

        builder.Property(rt => rt.UsedAt)
            .HasColumnName("used_at");

        builder.Property(rt => rt.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(rt => rt.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasMaxLength(255);

        builder.Property(rt => rt.ReplacedByToken)
            .HasColumnName("replaced_by_token")
            .HasMaxLength(512);

        builder.Property(rt => rt.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(rt => rt.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(512);

        builder.Property(rt => rt.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(128);

        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(rt => rt.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(rt => rt.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(rt => rt.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(rt => rt.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Ignore(rt => rt.IsExpired);
        builder.Ignore(rt => rt.IsActive);
        builder.Ignore(rt => rt.DomainEvents);

        builder.HasIndex(rt => rt.Token).IsUnique();
        builder.HasIndex(rt => rt.AccountId);
        builder.HasIndex(rt => rt.JwtId);
        builder.HasIndex(rt => rt.Status);
        builder.HasIndex(rt => rt.ExpiredAt);
        builder.HasIndex(rt => new { rt.AccountId, rt.Status });

        builder.HasQueryFilter(rt => !rt.IsDeleted);

        builder.HasOne(rt => rt.Account)
            .WithMany(a => a.RefreshTokens)
            .HasForeignKey(rt => rt.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
