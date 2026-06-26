using AuditAggregatorService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditAggregatorService.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping cho <see cref="AuditAggregate"/> → bảng <c>audit_aggregate</c> (Sprint audit <c>#AUDIT-14</c>).
///
/// <para><b>Lưu ý partitioning:</b> migration sẽ override CREATE TABLE thành partitioned (PARTITION BY RANGE
/// (occurred_at)) + pg_partman + composite unique <c>(event_id, occurred_at)</c> — Postgres bắt buộc unique key
/// trên partitioned table phải chứa partition key. EF model chỉ mô tả schema logic.</para>
/// </summary>
public class AuditAggregateConfiguration : IEntityTypeConfiguration<AuditAggregate>
{
    public void Configure(EntityTypeBuilder<AuditAggregate> b)
    {
        b.ToTable("audit_aggregate");
        // Composite PK (id, occurred_at) — Postgres bắt buộc PK partitioned table phải chứa partition key (occurred_at).
        b.HasKey(x => new { x.Id, x.OccurredAt });
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

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

        // AuditableEntity columns (nhất quán các bảng audit khác). CreatedAt = thời điểm ingest read-store.
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz")
            .HasDefaultValueSql("now()").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

        b.Property(x => x.GeoCountry).HasColumnName("geo_country").HasMaxLength(2);
        b.Property(x => x.GeoCity).HasColumnName("geo_city").HasMaxLength(100);

        // Indexes — migration sẽ tạo lại trên partitioned parent. Khai báo ở model cho EF query optimization hint.
        b.HasIndex(x => new { x.EventId, x.OccurredAt }).IsUnique().HasDatabaseName("ux_agg_event_occurred");
        b.HasIndex(x => new { x.ServiceName, x.OccurredAt }).HasDatabaseName("ix_agg_service");
        b.HasIndex(x => new { x.ActorAccountId, x.OccurredAt }).HasDatabaseName("ix_agg_actor");
        b.HasIndex(x => x.CorrelationId).HasDatabaseName("ix_agg_correlation");
        b.HasIndex(x => new { x.Severity, x.OccurredAt }).HasDatabaseName("ix_agg_severity");
    }
}
