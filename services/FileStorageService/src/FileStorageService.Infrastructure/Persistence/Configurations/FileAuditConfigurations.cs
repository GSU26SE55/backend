using FileStorageService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStorageService.Infrastructure.Persistence.Configurations;

/// <summary>Map <see cref="FileAuditLog"/> → bảng <c>file_audit_logs</c> (Sprint audit #AUDIT-29).</summary>
public class FileAuditLogConfiguration : IEntityTypeConfiguration<FileAuditLog>
{
    public void Configure(EntityTypeBuilder<FileAuditLog> b)
    {
        b.ToTable("file_audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
        b.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(50).IsRequired();
        b.Property(x => x.ActionCode).HasColumnName("action_code").HasMaxLength(100).IsRequired();
        b.Property(x => x.ActionCategory).HasColumnName("action_category").HasMaxLength(50).IsRequired();
        b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        b.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(50);
        b.Property(x => x.TargetId).HasColumnName("target_id");
        b.Property(x => x.TargetDisplay).HasColumnName("target_display").HasMaxLength(255);
        b.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
        b.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(50);
        b.Property(x => x.ActorDisplay).HasColumnName("actor_display").HasMaxLength(255);
        b.Property(x => x.ActorIp).HasColumnName("actor_ip").HasMaxLength(64);
        b.Property(x => x.ActorUserAgent).HasColumnName("actor_user_agent").HasMaxLength(512);
        b.Property(x => x.IsSuccess).HasColumnName("is_success").IsRequired();
        b.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(50);
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1024);
        b.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.CausationId).HasColumnName("causation_id");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("ux_file_audit_logs_event_id");
        b.HasIndex(x => new { x.ActionCode, x.CreatedAt }).HasDatabaseName("ix_file_audit_logs_action");
        b.HasIndex(x => new { x.TargetId, x.CreatedAt }).HasDatabaseName("ix_file_audit_logs_target");
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>Map <see cref="FileAuditOutbox"/> → bảng <c>file_audit_outbox</c> (Sprint audit #AUDIT-29).</summary>
public class FileAuditOutboxConfiguration : IEntityTypeConfiguration<FileAuditOutbox>
{
    public void Configure(EntityTypeBuilder<FileAuditOutbox> b)
    {
        b.ToTable("file_audit_outbox");
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
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("ux_file_audit_outbox_event_id");
        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_file_audit_outbox_pending").HasFilter("status = 1");
        b.Ignore(x => x.DomainEvents);
    }
}
