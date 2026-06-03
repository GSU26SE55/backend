using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class SlaTimerConfiguration : IEntityTypeConfiguration<SlaTimer>
{
    public void Configure(EntityTypeBuilder<SlaTimer> builder)
    {
        builder.ToTable("sla_timers");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.Priority)
            .HasColumnName("priority")
            .HasConversion<int>();

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at");

        builder.Property(e => e.DueAt)
            .HasColumnName("due_at");

        builder.Property(e => e.OriginalDueAt)
            .HasColumnName("original_due_at");

        builder.Property(e => e.TotalPausedMinutes)
            .HasColumnName("total_paused_minutes");

        builder.Property(e => e.CurrentPauseStartedAt)
            .HasColumnName("current_pause_started_at");

        builder.Property(e => e.WarningSentAt)
            .HasColumnName("warning_sent_at");

        builder.Property(e => e.BreachAt)
            .HasColumnName("breach_at");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(e => e.MaxTotalPauseMinutes)
            .HasColumnName("max_total_pause_minutes");

        builder.Property(e => e.MaxPauseEpisodes)
            .HasColumnName("max_pause_episodes");

        builder.Property(e => e.PauseEpisodesCount)
            .HasColumnName("pause_episodes_count");

        builder.Property(e => e.LastAutoResumeAt)
            .HasColumnName("last_auto_resume_at");

        builder.Property(e => e.ApprovalRequired)
            .HasColumnName("approval_required");

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Ticket)
            .WithOne(e => e.SlaTimer)
            .HasForeignKey<SlaTimer>(e => e.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
