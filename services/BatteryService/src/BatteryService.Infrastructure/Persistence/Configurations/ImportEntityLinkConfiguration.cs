using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class ImportEntityLinkConfiguration : IEntityTypeConfiguration<ImportEntityLink>
{
    public void Configure(EntityTypeBuilder<ImportEntityLink> builder)
    {
        builder.ToTable("import_entity_links");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.EntityType).HasColumnName("entity_type").HasConversion<int>();
        builder.Property(l => l.ExternalRef).HasColumnName("external_ref").HasMaxLength(128).IsRequired();
        builder.Property(l => l.ExternalRefRaw).HasColumnName("external_ref_raw").HasMaxLength(128).IsRequired();
        builder.Property(l => l.InternalId).HasColumnName("internal_id").IsRequired();
        builder.Property(l => l.CreatedByBatchId).HasColumnName("created_by_batch_id");

        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");

        // Khoá tự nhiên của toàn bộ cơ chế chống nhân bản khi nạp lại.
        //
        // Cả hai cột đều bắt buộc, nên ràng buộc này thực sự có hiệu lực. Bản trước có thêm cột
        // định danh bên cung cấp cho phép rỗng, và PostgreSQL coi mỗi giá trị rỗng là khác nhau —
        // nghĩa là ràng buộc duy nhất KHÔNG chặn được gì cho đúng đường đi phổ biến nhất.
        //
        // Lọc theo is_deleted vì liên kết bị hoàn tác phải nhường chỗ cho lần nạp sau: không lọc
        // thì một lần hoàn tác là ô đó bị chiếm vĩnh viễn và lần nạp lại luôn đâm vào lỗi trùng khoá.
        builder.HasIndex(l => new { l.EntityType, l.ExternalRef })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_import_entity_links_entity_ref");

        builder.HasIndex(l => l.InternalId)
            .HasDatabaseName("idx_import_entity_links_internal_id");

        builder.HasIndex(l => l.CreatedByBatchId)
            .HasDatabaseName("idx_import_entity_links_batch");
    }
}
