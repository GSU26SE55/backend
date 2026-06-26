using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

/// <summary>Map <see cref="BatteryAuditOutbox"/> → bảng <c>battery_audit_outbox</c> (Sprint audit #AUDIT-21).</summary>
public class BatteryAuditOutboxConfiguration : IEntityTypeConfiguration<BatteryAuditOutbox>
{
    public void Configure(EntityTypeBuilder<BatteryAuditOutbox> b)
    {
        b.ToTable("battery_audit_outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz");
        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(x => x.LastError).HasColumnName("last_error");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("ux_battery_audit_outbox_event_id");
        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_battery_audit_outbox_pending").HasFilter("status = 1");

        b.Ignore(x => x.DomainEvents);
    }
}
