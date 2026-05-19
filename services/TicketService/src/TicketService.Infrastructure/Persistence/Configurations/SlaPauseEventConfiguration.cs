using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class SlaPauseEventConfiguration : IEntityTypeConfiguration<SlaPauseEvent>
{
    public void Configure(EntityTypeBuilder<SlaPauseEvent> builder)
    {
        builder.ToTable("sla_pause_events");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.SlaTimerId)
            .HasColumnName("sla_timer_id");

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasConversion<int>();

        builder.Property(e => e.Note)
            .HasColumnName("note");

        builder.Property(e => e.PausedAt)
            .HasColumnName("paused_at");

        builder.Property(e => e.PausedByUserId)
            .HasColumnName("paused_by_user_id");

        builder.Property(e => e.ResumedAt)
            .HasColumnName("resumed_at");

        builder.Property(e => e.ResumedByUserId)
            .HasColumnName("resumed_by_user_id");

        builder.Property(e => e.DurationMinutes)
            .HasColumnName("duration_minutes");

        builder.Property(e => e.IsApprovedByManager)
            .HasColumnName("is_approved_by_manager");

        builder.Property(e => e.ApprovedByManagerId)
            .HasColumnName("approved_by_manager_id");

        builder.Property(e => e.AutoResumeReason)
            .HasColumnName("auto_resume_reason");

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

        builder.HasIndex(e => e.SlaTimerId);

        builder.HasOne(e => e.SlaTimer)
            .WithMany()
            .HasForeignKey(e => e.SlaTimerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
