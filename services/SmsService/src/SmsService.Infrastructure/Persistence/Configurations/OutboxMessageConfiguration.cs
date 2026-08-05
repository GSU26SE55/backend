using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(o => o.Id);

        b.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(500).IsRequired();
        b.Property(o => o.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();
        b.Property(o => o.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(o => o.ProcessedAt).HasColumnName("processed_at");
        b.Property(o => o.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(o => o.LastError).HasColumnName("last_error").HasMaxLength(2000);

        b.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(o => o.CreatedBy).HasColumnName("created_by");
        b.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        b.Property(o => o.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(o => o.DeletedAt).HasColumnName("deleted_at");

        // GH-794 — quyền giữ dòng (lease) để hai replica không cùng publish một message.
        b.Property(o => o.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        b.Property(o => o.LeaseUntilUtc).HasColumnName("lease_until_utc");

        b.HasIndex(o => new { o.ProcessedAt, o.OccurredAt })
            .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");

        // Chỉ mục cho đúng câu truy vấn "dòng nào nhận được": chưa xử lý VÀ chưa ai giữ (hoặc quyền
        // đã hết hạn). Lọc theo processed_at IS NULL để chỉ mục chỉ chứa phần việc còn tồn.
        b.HasIndex(o => new { o.ProcessedAt, o.LeaseUntilUtc, o.OccurredAt })
            .HasDatabaseName("idx_outbox_claimable")
            .HasFilter("processed_at IS NULL");

        b.Ignore(o => o.DomainEvents);
    }
}
