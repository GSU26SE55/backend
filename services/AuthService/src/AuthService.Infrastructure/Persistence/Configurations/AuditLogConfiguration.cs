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
