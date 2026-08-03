using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.4 NOTI4-06 — bảng nối nhiều-nhiều lần gửi ↔ nhóm.</summary>
public class NotificationBatchTargetConfiguration : IEntityTypeConfiguration<NotificationBatchTarget>
{
    public void Configure(EntityTypeBuilder<NotificationBatchTarget> builder)
    {
        // Đúng một trong hai cột phải có giá trị. Đặt ở DB vì đây là bất biến của mô hình, không
        // phải luật nghiệp vụ có thể đổi.
        builder.ToTable("notification_batch_targets", t => t.HasCheckConstraint(
            "ck_notification_batch_targets_shape",
            "(target_kind = 1 AND group_id IS NOT NULL AND user_id IS NULL) " +
            "OR (target_kind = 2 AND user_id IS NOT NULL AND group_id IS NULL)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.TargetKind).HasColumnName("target_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(x => x.BatchId)
               .HasDatabaseName("ix_notification_batch_targets_batch");

        // Chiều "nhóm này đã nhận những lần gửi nào" — chính là quan hệ 1 nhóm → nhiều thông báo.
        builder.HasIndex(x => x.GroupId)
               .HasFilter("group_id IS NOT NULL")
               .HasDatabaseName("ix_notification_batch_targets_group");

        // RESTRICT chứ không CASCADE: xoá nhóm KHÔNG được xoá lịch sử đã gửi cho nhóm đó. Nhóm chỉ
        // xoá mềm nên ràng buộc này không bao giờ chặn thao tác thật, nó chỉ chặn xoá cứng nhầm.
        builder.HasOne(x => x.Group)
               .WithMany()
               .HasForeignKey(x => x.GroupId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.DomainEvents);
    }
}
