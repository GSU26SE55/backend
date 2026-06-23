using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class ChatMetricsDailyConfiguration : IEntityTypeConfiguration<ChatMetricsDaily>
{
    public void Configure(EntityTypeBuilder<ChatMetricsDaily> builder)
    {
        builder.ToTable("chat_metrics_daily");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.MetricDate)
            .HasColumnName("metric_date")
            .HasColumnType("date");

        builder.Property(e => e.StaffId)
            .HasColumnName("staff_id");

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.ChatCount)
            .HasColumnName("chat_count")
            .HasDefaultValue(0);

        builder.Property(e => e.AvgResponseTimeMin)
            .HasColumnName("avg_response_time_min")
            .HasDefaultValue(0);

        builder.Property(e => e.InternalCount)
            .HasColumnName("internal_count")
            .HasDefaultValue(0);

        builder.Property(e => e.MentionReceivedCount)
            .HasColumnName("mention_received_count")
            .HasDefaultValue(0);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(e => e.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted");

        builder.Property(e => e.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(e => new { e.MetricDate, e.StaffId })
            .HasDatabaseName("ix_chat_metrics_daily_date_staff");
    }
}
