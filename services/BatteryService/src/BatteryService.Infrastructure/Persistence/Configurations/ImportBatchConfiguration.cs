using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("import_batches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(b => b.FileName).HasColumnName("file_name").HasMaxLength(255);
        builder.Property(b => b.FileSha256).HasColumnName("file_sha256").HasMaxLength(64).IsRequired();
        builder.Property(b => b.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(b => b.IsDryRun).HasColumnName("is_dry_run");
        builder.Property(b => b.RequestedBy).HasColumnName("requested_by");
        builder.Property(b => b.EntityTypesMask).HasColumnName("entity_types_mask");
        builder.Property(b => b.TotalRows).HasColumnName("total_rows");
        builder.Property(b => b.ValidRows).HasColumnName("valid_rows");
        builder.Property(b => b.InvalidRows).HasColumnName("invalid_rows");
        builder.Property(b => b.CreatedRows).HasColumnName("created_rows");
        builder.Property(b => b.UpdatedRows).HasColumnName("updated_rows");
        builder.Property(b => b.SkippedRows).HasColumnName("skipped_rows");
        builder.Property(b => b.FailedRows).HasColumnName("failed_rows");
        builder.Property(b => b.StartedAt).HasColumnName("started_at");
        builder.Property(b => b.CompletedAt).HasColumnName("completed_at");
        builder.Property(b => b.ErrorSummary).HasColumnName("error_summary").HasMaxLength(2000);

        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");
        builder.Property(b => b.CreatedBy).HasColumnName("created_by");
        builder.Property(b => b.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(b => b.DeletedAt).HasColumnName("deleted_at");

        // Chặn nạp lại đúng file đã nạp: tra theo hash nội dung.
        builder.HasIndex(b => b.FileSha256)
            .HasDatabaseName("idx_import_batches_file_sha256");

        // Background service quét lô theo trạng thái và lấy cũ nhất trước.
        builder.HasIndex(b => new { b.Status, b.CreatedAt })
            .HasDatabaseName("idx_import_batches_status_created_at");
    }
}
