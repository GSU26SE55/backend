using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class BackupCodeConfiguration : IEntityTypeConfiguration<BackupCode>
{
    public void Configure(EntityTypeBuilder<BackupCode> builder)
    {
        builder.ToTable("backup_codes");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(b => b.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(b => b.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(b => b.RedeemedAt)
            .HasColumnName("redeemed_at");

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(b => b.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(b => b.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(b => b.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(b => new { b.AccountId, b.RedeemedAt });
        builder.HasIndex(b => b.IsDeleted);

        // FK Cascade: chỉ kích hoạt khi Account bị HARD DELETE (rare).
        // Soft-delete (DeleteAsync qua AuditableEntityInterceptor) sẽ chuyển sang UPDATE IsDeleted=true
        // → cascade KHÔNG chạy. Backup codes cũ vẫn còn để audit trail.
        // Handler Disable2FA + AdminReset2FA + RegenerateBackupCodes tự xóa codes thủ công.
        builder.HasOne(b => b.Account)
            .WithMany(a => a.BackupCodes)
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(b => !b.IsDeleted);

        builder.Ignore(b => b.DomainEvents);
    }
}
