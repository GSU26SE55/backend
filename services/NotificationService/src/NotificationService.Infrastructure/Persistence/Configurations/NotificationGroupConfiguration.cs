using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.4 NOTI4-01 — nhóm người nhận thông báo.</summary>
public class NotificationGroupConfiguration : IEntityTypeConfiguration<NotificationGroup>
{
    public void Configure(EntityTypeBuilder<NotificationGroup> builder)
    {
        // CHECK constraint giữ hai loại nhóm không lẫn vào nhau: nhóm Role (kind = 2) BẮT BUỘC có
        // role_filter, nhóm Static (kind = 1) BẮT BUỘC không có. Đặt ở DB chứ không chỉ ở handler vì
        // seeder ghi thẳng DbContext, không đi qua tầng validate nào.
        builder.ToTable("notification_groups", t => t.HasCheckConstraint(
            "ck_notification_groups_role_filter",
            "(kind = 2 AND role_filter IS NOT NULL) OR (kind = 1 AND role_filter IS NULL)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(512);
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.RoleFilter).HasColumnName("role_filter").HasMaxLength(64);
        builder.Property(x => x.IsSystem).HasColumnName("is_system").HasDefaultValue(false);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Chống trùng tên không phân biệt hoa-thường. Partial (chỉ dòng chưa xoá) vì dự án dùng soft
        // delete: xoá nhóm rồi tạo lại đúng tên đó phải được.
        //
        // ⚠️ Partial unique index của Postgres KHÔNG deferrable. Thao tác nào vừa nhả tên cũ vừa
        // chiếm tên đó cho dòng khác phải lưu HAI LẦN riêng trong cùng transaction, nhả trước —
        // bài học đã trả giá ở ux_notification_templates_active_per_key.
        builder.HasIndex(x => x.NormalizedName)
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ux_notification_groups_normalized_name");

        // Mỗi role chỉ có đúng một nhóm động ⇒ seeder chạy lại bao nhiêu lần cũng không đẻ thêm.
        // Kind = 2 là NotificationGroupKindEnum.Role (viết số vì filter là SQL thô).
        builder.HasIndex(x => x.RoleFilter)
               .IsUnique()
               .HasFilter("kind = 2 AND is_deleted = false")
               .HasDatabaseName("ux_notification_groups_role_filter");

        builder.HasMany(x => x.Members)
               .WithOne(m => m.Group!)
               .HasForeignKey(m => m.GroupId)
               // Khoá ngoại THẬT: cả hai bảng cùng notification_db, cùng transaction, không có gì
               // bất định. Khác hẳn NotificationGroupMember.UserId — chỗ đó trỏ sang read-model đồng
               // bộ qua bus nên cố ý KHÔNG đặt khoá ngoại.
               .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}
