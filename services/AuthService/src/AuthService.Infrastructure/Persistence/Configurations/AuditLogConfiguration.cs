using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.TargetAccountId)
            .HasColumnName("target_account_id");

        builder.Property(a => a.TargetEmail)
            .HasColumnName("target_email")
            .HasMaxLength(256);

        builder.Property(a => a.ActorAccountId)
            .HasColumnName("actor_account_id");

        builder.Property(a => a.IsSuccess)
            .HasColumnName("is_success")
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.Property(a => a.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("text");

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500);

        builder.Property(a => a.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(128);

        builder.Property(a => a.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(64);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(a => a.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(a => a.DeletedAt)
            .HasColumnName("deleted_at");

        // ===== Sprint audit #AUDIT-06 — 14 cột chuẩn =====
        builder.Property(a => a.EventId).HasColumnName("event_id");
        builder.Property(a => a.ServiceName).HasColumnName("service_name").HasMaxLength(50);
        builder.Property(a => a.ActionCode).HasColumnName("action_code").HasMaxLength(100);
        builder.Property(a => a.ActionCategory).HasColumnName("action_category").HasMaxLength(50);
        builder.Property(a => a.Severity).HasColumnName("severity").HasMaxLength(20);
        builder.Property(a => a.TargetType).HasColumnName("target_type").HasMaxLength(50);
        builder.Property(a => a.TargetId).HasColumnName("target_id");
        builder.Property(a => a.TargetDisplay).HasColumnName("target_display").HasMaxLength(255);
        builder.Property(a => a.ActorRole).HasColumnName("actor_role").HasMaxLength(50);
        builder.Property(a => a.ActorDisplay).HasColumnName("actor_display").HasMaxLength(255);
        builder.Property(a => a.ErrorCode).HasColumnName("error_code").HasMaxLength(50);
        builder.Property(a => a.CausationId).HasColumnName("causation_id");
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz");
        builder.Property(a => a.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamptz");

        builder.HasIndex(a => a.EventId).IsUnique().HasDatabaseName("ux_audit_logs_event_id");

        builder.HasIndex(a => a.TargetAccountId)
            .HasDatabaseName("ix_audit_logs_target_account_id");

        builder.HasIndex(a => a.ActorAccountId)
            .HasDatabaseName("ix_audit_logs_actor_account_id");

        builder.HasIndex(a => a.Action)
            .HasDatabaseName("ix_audit_logs_action");

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("ix_audit_logs_created_at");

        builder.HasIndex(a => new { a.TargetAccountId, a.CreatedAt })
            .HasDatabaseName("ix_audit_logs_target_created_at");

        builder.HasQueryFilter(a => !a.IsDeleted);

        builder.Ignore(a => a.DomainEvents);
    }
}
