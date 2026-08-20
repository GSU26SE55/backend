using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.ToTable("import_rows");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.ImportBatchId).HasColumnName("import_batch_id").IsRequired();
        builder.Property(r => r.RowNumber).HasColumnName("row_number");
        builder.Property(r => r.EntityType).HasColumnName("entity_type").HasConversion<int>();
        builder.Property(r => r.RawJson).HasColumnName("raw_json").HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ExternalRef).HasColumnName("external_ref").HasMaxLength(128).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(r => r.ErrorsJson).HasColumnName("errors_json").HasColumnType("jsonb");
        builder.Property(r => r.CreatedEntityId).HasColumnName("created_entity_id");
        builder.Property(r => r.LinkedAccountId).HasColumnName("linked_account_id");
        builder.Property(r => r.ProcessedAt).HasColumnName("processed_at");
        builder.Property(r => r.AttemptCount).HasColumnName("attempt_count");

        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(r => r.Batch)
            .WithMany(b => b.Rows)
            .HasForeignKey(r => r.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Một số dòng chỉ xuất hiện đúng một lần trong một lô — chặn ghi trùng khi parse chạy lại.
        builder.HasIndex(r => new { r.ImportBatchId, r.EntityType, r.RowNumber })
            .IsUnique()
            .HasDatabaseName("ux_import_rows_batch_entity_row");

        // Màn xem lỗi và background service đều lọc theo (lô, trạng thái).
        builder.HasIndex(r => new { r.ImportBatchId, r.Status })
            .HasDatabaseName("idx_import_rows_batch_status");

        builder.HasIndex(r => new { r.ImportBatchId, r.EntityType, r.ExternalRef })
            .HasDatabaseName("idx_import_rows_batch_entity_ref");
    }
}
