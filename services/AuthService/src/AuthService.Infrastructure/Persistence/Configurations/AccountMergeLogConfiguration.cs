using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

/// <summary>
/// #AUTH-47: EF Config cho <see cref="AccountMergeLog"/>.
/// Append-only — bảo vệ qua trigger trong migration (tương tự AUTH-29 cho audit_logs).
/// </summary>
public class AccountMergeLogConfiguration : IEntityTypeConfiguration<AccountMergeLog>
{
    public void Configure(EntityTypeBuilder<AccountMergeLog> builder)
    {
        builder.ToTable("account_merge_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.PrimaryAccountId).HasColumnName("primary_account_id").IsRequired();
        builder.Property(x => x.SecondaryAccountId).HasColumnName("secondary_account_id").IsRequired();
        builder.Property(x => x.PerformedBy).HasColumnName("performed_by").IsRequired();

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SecondaryAccountSnapshotJson)
            .HasColumnName("secondary_account_snapshot_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(x => x.ConflictResolutionJson)
            .HasColumnName("conflict_resolution_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.SessionsRevoked).HasColumnName("sessions_revoked").HasDefaultValue(0);
        builder.Property(x => x.AuditLogsLinked).HasColumnName("audit_logs_linked").HasDefaultValue(0);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(x => x.PrimaryAccountId).HasDatabaseName("ix_account_merge_logs_primary");
        builder.HasIndex(x => x.SecondaryAccountId).HasDatabaseName("ix_account_merge_logs_secondary");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
