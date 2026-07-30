using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.3 NOTI3-02 (#702).</summary>
public class PushReceiptConfiguration : IEntityTypeConfiguration<PushReceipt>
{
    public void Configure(EntityTypeBuilder<PushReceipt> builder)
    {
        builder.ToTable("push_receipts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.TicketId).HasColumnName("ticket_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DeviceToken).HasColumnName("device_token").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        builder.Property(x => x.CheckedAt).HasColumnName("checked_at");
        builder.Property(x => x.CheckAttemptCount).HasColumnName("check_attempt_count").HasDefaultValue(0);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Cùng một ticket id không được đối soát hai lần (Expo trả id duy nhất cho mỗi message).
        builder.HasIndex(x => x.TicketId).IsUnique().HasDatabaseName("ux_push_receipts_ticket_id");

        // Worker quét: lấy receipt Pending cũ nhất → index đúng chiều truy vấn.
        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_push_receipts_status_created");

        // NOTI3-05: tra "notification này đã có receipt ok chưa" khi cân nhắc fallback sang SMS.
        builder.HasIndex(x => x.NotificationId).HasDatabaseName("ix_push_receipts_notification_id");

        builder.Ignore(x => x.DomainEvents);
    }
}
