using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);

        builder.Property(a => a.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(a => a.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(500);

        builder.Property(a => a.DateOfBirth)
            .HasColumnName("date_of_birth")
            .HasColumnType("date");

        builder.Property(a => a.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(a => a.EmailConfirmed)
            .HasColumnName("email_confirmed")
            .HasDefaultValue(false);

        builder.Property(a => a.PhoneConfirmed)
            .HasColumnName("phone_confirmed")
            .HasDefaultValue(false);

        builder.Property(a => a.TwoFactorEnabled)
            .HasColumnName("two_factor_enabled")
            .HasDefaultValue(false);

        builder.Property(a => a.OtpCode)
            .HasColumnName("otp_code")
            .HasMaxLength(10);

        builder.Property(a => a.OtpExpiredAt)
            .HasColumnName("otp_expired_at");

        builder.Property(a => a.OtpPurpose)
            .HasColumnName("otp_purpose")
            .HasConversion<int?>();

        builder.Property(a => a.PendingEmail)
            .HasColumnName("pending_email")
            .HasMaxLength(256);

        builder.Property(a => a.TwoFactorSecret)
            .HasColumnName("two_factor_secret")
            .HasMaxLength(256);

        builder.Property(a => a.FailedLoginAttempts)
            .HasColumnName("failed_login_attempts")
            .HasDefaultValue(0);

        builder.Property(a => a.LockoutEndAt)
            .HasColumnName("lockout_end_at");

        builder.Property(a => a.LastLoginAt)
            .HasColumnName("last_login_at");

        builder.Property(a => a.LastLoginIp)
            .HasColumnName("last_login_ip")
            .HasMaxLength(45);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(AccountStatusEnum.PendingVerification)
            .IsRequired();

        builder.Property(a => a.GoogleId)
            .HasColumnName("google_id")
            .HasMaxLength(128);

        builder.Property(a => a.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50);

        builder.Property(a => a.InvitationToken)
            .HasColumnName("invitation_token")
            .HasMaxLength(128);

        builder.Property(a => a.InvitationExpiredAt)
            .HasColumnName("invitation_expired_at");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(a => a.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(a => a.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(a => a.Email).IsUnique();
        builder.HasIndex(a => a.PhoneNumber).IsUnique().HasFilter("\"phone_number\" IS NOT NULL");
        builder.HasIndex(a => a.GoogleId).IsUnique().HasFilter("\"google_id\" IS NOT NULL");
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.IsDeleted);
        builder.HasIndex(a => a.InvitationToken).HasFilter("\"invitation_token\" IS NOT NULL");

        builder.HasQueryFilter(a => !a.IsDeleted);

        builder.HasMany(a => a.AccountRoles)
            .WithOne(ar => ar.Account)
            .HasForeignKey(ar => ar.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.RefreshTokens)
            .WithOne(rt => rt.Account)
            .HasForeignKey(rt => rt.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(a => a.DomainEvents);
    }
}
