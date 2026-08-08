using AuditAggregatorService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditAggregatorService.Infrastructure.Persistence.Configurations;

/// <summary>
/// GH-728 — EF mapping cho <see cref="AuditReplayJob"/> → bảng <c>audit_replay_job</c>.
///
/// <para>Bảng thường, KHÔNG partition (khác <c>audit_aggregate</c>): số job replay rất nhỏ
/// và không có partition key thời gian nào đáng dùng.</para>
/// </summary>
public class AuditReplayJobConfiguration : IEntityTypeConfiguration<AuditReplayJob>
{
    [Obsolete]
    public void Configure(EntityTypeBuilder<AuditReplayJob> b)
    {
        b.ToTable("audit_replay_job");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(50);
        b.Property(x => x.FromUtc).HasColumnName("from_utc");
        b.Property(x => x.ToUtc).HasColumnName("to_utc");

        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.ExpectedResponders).HasColumnName("expected_responders").IsRequired();
        b.Property(x => x.RespondedCount).HasColumnName("responded_count").IsRequired();
        b.Property(x => x.RepublishedCount).HasColumnName("republished_count").IsRequired();
        b.Property(x => x.Truncated).HasColumnName("truncated").IsRequired();
        b.Property(x => x.RespondedServices).HasColumnName("responded_services").HasMaxLength(512).IsRequired();
        b.Property(x => x.Error).HasColumnName("error").HasMaxLength(4000);

        b.Property(x => x.RequestedByAccountId).HasColumnName("requested_by_account_id");
        b.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        b.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");

        // AuditableEntity
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // GH-728 — 6 service báo "xong" ĐỒNG THỜI vào cùng một dòng job. Không có token
        // đồng thời thì last-write-wins và số đếm bị nuốt mất (job treo mãi ở InProgress).
        // xmin của Postgres cho phép phát hiện xung đột mà không cần thêm cột.
        b.UseXminAsConcurrencyToken();

        // Tra job mới nhất trước — màn hình admin luôn hỏi "lần replay gần đây thế nào".
        b.HasIndex(x => x.RequestedAtUtc).HasDatabaseName("ix_audit_replay_job_requested_at");
        // Tìm job chưa xong (giám sát job treo).
        b.HasIndex(x => x.Status).HasDatabaseName("ix_audit_replay_job_status");
    }
}
