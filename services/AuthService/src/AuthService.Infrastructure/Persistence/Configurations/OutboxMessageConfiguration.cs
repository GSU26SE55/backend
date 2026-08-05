using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(o => o.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(o => o.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(o => o.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        builder.Property(o => o.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2000);

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(o => o.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(o => o.DeletedAt)
            .HasColumnName("deleted_at");

        // GH-794 — quyền giữ dòng (lease) để hai replica không cùng publish một message.
        builder.Property(o => o.LeaseOwner)
            .HasColumnName("lease_owner")
            .HasMaxLength(128);

        builder.Property(o => o.LeaseUntilUtc)
            .HasColumnName("lease_until_utc");

        builder.HasIndex(o => new { o.ProcessedAt, o.OccurredAt })
            .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");

        // Chỉ mục cho đúng câu truy vấn "dòng nào nhận được": chưa xử lý VÀ chưa ai giữ (hoặc quyền
        // đã hết hạn). Lọc theo processed_at IS NULL để chỉ mục chỉ chứa phần việc còn tồn.
        builder.HasIndex(o => new { o.ProcessedAt, o.LeaseUntilUtc, o.OccurredAt })
            .HasDatabaseName("idx_outbox_claimable")
            .HasFilter("processed_at IS NULL");

        builder.Ignore(o => o.DomainEvents);
    }
}
