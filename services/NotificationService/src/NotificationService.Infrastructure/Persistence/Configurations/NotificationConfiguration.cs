using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(n => n.Type)
            .HasColumnName("type").HasConversion<int>().IsRequired();

        builder.Property(n => n.Channel)
            .HasColumnName("channel").HasConversion<int>().IsRequired();

        builder.Property(n => n.Status)
            .HasColumnName("status").HasConversion<int>()
            .HasDefaultValue(NotificationStatusEnum.Pending).IsRequired();

        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(n => n.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(n => n.EntityType).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(n => n.EntityId).HasColumnName("entity_id");
        builder.Property(n => n.BatchId).HasColumnName("batch_id");   // Sprint 6.4 NOTI4-06
        builder.Property(n => n.SentAt).HasColumnName("sent_at");
        builder.Property(n => n.ReadAt).HasColumnName("read_at");
        builder.Property(n => n.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);

        // Sprint 6.2 NOTI-01 (#672) — trạng thái retry/hoãn của dispatch worker.
        builder.Property(n => n.DispatchAttemptCount)
            .HasColumnName("dispatch_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(n => n.NextAttemptAt).HasColumnName("next_attempt_at");

        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.CreatedBy).HasColumnName("created_by");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");
        builder.Property(n => n.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(n => n.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(n => new { n.UserId, n.Status });
        builder.HasIndex(n => n.CreatedAt);
        builder.HasIndex(n => new { n.EntityType, n.EntityId });

        // Sprint 6.2 NOTI-01 (#672) — index cho vòng quét của dispatch worker
        // (WHERE status = Pending AND next_attempt_at <= now ORDER BY created_at).
        builder.HasIndex(n => new { n.Status, n.NextAttemptAt, n.CreatedAt })
            .HasDatabaseName("ix_notifications_dispatch_queue");

        // Sprint 6.4 NOTI4-06 — "lần gửi này đã tới ai, bao nhiêu người đã đọc".
        // Partial vì phần lớn dòng cũ có batch_id NULL (xem Notification.BatchId).
        builder.HasIndex(n => n.BatchId)
            .HasFilter("batch_id IS NOT NULL")
            .HasDatabaseName("ix_notifications_batch");

        // LỚP CHỐNG NHẬN TRÙNG THỨ HAI (R-47). Tầng ứng dụng đã gom trùng bằng DISTINCT ở
        // RecipientResolver.GetGroupRecipientsAsync, nhưng nếu chỗ đó sai thì DB vẫn chặn — người ở
        // hai nhóm cùng được nhắm không thể nhận hai lần trong cùng một lần gửi.
        builder.HasIndex(n => new { n.BatchId, n.UserId, n.Channel })
            .IsUnique()
            .HasFilter("batch_id IS NOT NULL")
            .HasDatabaseName("ux_notifications_batch_user_channel");

        // Khoá ngoại KHÔNG kèm navigation property: bảng notifications là đường đọc nóng nhất hệ
        // thống, có navigation là sớm muộn cũng có người Include nó rồi kéo theo cả nội dung batch
        // cho từng dòng feed. HasOne<T>() không tham số khai được quan hệ mà không sinh navigation.
        //
        // SetNull chứ không Cascade: xoá một lần gửi KHÔNG được xoá thông báo đã nằm trong hộp thư
        // người dùng — họ đã đọc rồi, xoá đi là mất dữ liệu của họ vì thao tác quản trị.
        builder.HasOne<NotificationBatch>()
            .WithMany()
            .HasForeignKey(n => n.BatchId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(n => n.DomainEvents);
    }
}
