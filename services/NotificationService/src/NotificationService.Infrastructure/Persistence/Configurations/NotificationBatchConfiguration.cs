using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.4 NOTI4-06 — nội dung một lần gửi, lưu đúng một lần.</summary>
public class NotificationBatchConfiguration : IEntityTypeConfiguration<NotificationBatch>
{
    public void Configure(EntityTypeBuilder<NotificationBatch> builder)
    {
        builder.ToTable("notification_batches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        // Giới hạn khớp cột tương ứng của notifications — nội dung được chép sang từng dòng người
        // nhận, dài hơn thì chỗ kia sẽ vỡ.
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(x => x.EntityId).HasColumnName("entity_id");

        // Mảng số nguyên: Npgsql map thẳng sang integer[], không cần value converter. Dùng
        // converter enum[] → int[] ở đây sẽ làm vỡ provider InMemory của test (xem NotificationBatch).
        builder.Property(x => x.ChannelValues)
               .HasColumnName("channels")
               .IsRequired();

        // Channels là lớp bọc kiểu, không có cột riêng.
        builder.Ignore(x => x.Channels);

        builder.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        builder.Property(x => x.TemplateId).HasColumnName("template_id");

        // Mặc định false: 2 lần gửi đã có trong DB đều là nội dung viết tay, không được đột ngột
        // chuyển sang render qua mẫu khi migration chạy.
        builder.Property(x => x.UseTemplate)
            .HasColumnName("use_template")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecipientCount).HasColumnName("recipient_count").HasDefaultValue(0);
        builder.Property(x => x.NotificationCount).HasColumnName("notification_count").HasDefaultValue(0);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Màn hình lịch sử gửi sắp xếp mới nhất trước.
        builder.HasIndex(x => x.CreatedAt)
               .IsDescending()
               .HasDatabaseName("ix_notification_batches_created_at");

        builder.HasIndex(x => new { x.EntityType, x.EntityId })
               .HasDatabaseName("ix_notification_batches_entity");

        builder.HasMany(x => x.Targets)
               .WithOne(t => t.Batch!)
               .HasForeignKey(t => t.BatchId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}
