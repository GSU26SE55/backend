using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.BatteryAssetId)
            .HasColumnName("battery_asset_id");
        builder.Property(e => e.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .IsRequired();

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasConversion<int>();

        builder.Property(e => e.Priority)
            .HasColumnName("priority")
            .HasConversion<int>();

        builder.Property(e => e.ImpactScope)
            .HasColumnName("impact_scope")
            .HasConversion<int>();

        builder.Property(e => e.UrgencyLevel)
            .HasColumnName("urgency_level")
            .HasConversion<int>();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(e => e.Origin)
            .HasColumnName("origin")
            .HasConversion<int>();

        builder.Property(e => e.OriginAlertId)
            .HasColumnName("origin_alert_id");

        // Sprint Bonus NS-22 (#662, E2) — link tới EnvironmentalIncident (site-level).
        builder.Property(e => e.EnvironmentalIncidentId)
            .HasColumnName("environmental_incident_id");

        builder.Property(e => e.SiteId)
            .HasColumnName("site_id");

        builder.Property(e => e.ReopenCount)
            .HasColumnName("reopen_count");

        builder.Property(e => e.ResolutionSummary)
            .HasColumnName("resolution_summary");

        builder.Property(e => e.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(e => e.ResolvedByStaffId)
            .HasColumnName("resolved_by_staff_id");

        builder.Property(e => e.ApprovedAt)
            .HasColumnName("approved_at");

        builder.Property(e => e.ApprovedByManagerId)
            .HasColumnName("approved_by_manager_id");

        builder.Property(e => e.Reason)
            .HasColumnName("reason");

        builder.Property(e => e.ClosedAt)
            .HasColumnName("closed_at");
        builder.Property(e => e.CloseReason)
            .HasColumnName("close_reason")
            .HasConversion<int>();

        builder.Property(e => e.Rating)
            .HasColumnName("rating");

        builder.Property(e => e.RatingComment)
            .HasColumnName("rating_comment");

        builder.Property(e => e.RatedAt)
            .HasColumnName("rated_at");

        builder.Property(e => e.EscalatedAt)
            .HasColumnName("escalated_at");

        builder.Property(e => e.EscalationReason)
            .HasColumnName("escalation_reason")
            .HasConversion<int>();

        builder.Property(e => e.IsIncident)
            .HasColumnName("is_incident");
        builder.Property(e => e.ScheduledStartAtUtc)
            .HasColumnName("scheduled_start_at_utc");
        builder.Property(e => e.ScheduleVersion)
            .HasColumnName("schedule_version")
            .HasDefaultValue(0);
        builder.Property(e => e.PeriodicMaintenanceDueAtUtc)
            .HasColumnName("periodic_maintenance_due_at_utc");
        builder.Property(e => e.PeriodicMaintenanceScheduleDeadlineAtUtc)
            .HasColumnName("periodic_maintenance_schedule_deadline_at_utc");
        builder.Property(e => e.PeriodicMaintenanceReminder1SentAtUtc)
            .HasColumnName("periodic_maintenance_reminder_1_sent_at_utc");
        builder.Property(e => e.PeriodicMaintenanceReminder2SentAtUtc)
            .HasColumnName("periodic_maintenance_reminder_2_sent_at_utc");
        builder.Property(e => e.PeriodicMaintenanceManagerEscalatedAtUtc)
            .HasColumnName("periodic_maintenance_manager_escalated_at_utc");
        builder.Property(e => e.PeriodicMaintenanceCustomerScheduledAtUtc)
            .HasColumnName("periodic_maintenance_customer_scheduled_at_utc");
        builder.Property(e => e.PendingContext)
            .HasColumnName("pending_context")
            .HasConversion<int>();
        builder.Property(e => e.PendingReason)
            .HasColumnName("pending_reason")
            .HasConversion<int>();
        builder.Property(e => e.ActiveIncidentEpisodeId)
            .HasColumnName("active_incident_episode_id");

        // GH-ticket-verify — DetectedAt + serial snapshot + AI verify + merge fields.
        builder.Property(e => e.DetectedAt)
            .HasColumnName("detected_at");

        builder.Property(e => e.BatterySerialNumber)
            .HasColumnName("battery_serial_number")
            .HasMaxLength(128);

        builder.Property(e => e.AiVerifyStatus)
            .HasColumnName("ai_verify_status")
            .HasConversion<int>();

        builder.Property(e => e.AiVerifyScore)
            .HasColumnName("ai_verify_score");

        builder.Property(e => e.AiVerifyReason)
            .HasColumnName("ai_verify_reason");

        builder.Property(e => e.SuspectedDuplicateOfTicketId)
            .HasColumnName("suspected_duplicate_of_ticket_id");

        builder.Property(e => e.DuplicateReason)
            .HasColumnName("duplicate_reason");

        builder.Property(e => e.MergedIntoTicketId)
            .HasColumnName("merged_into_ticket_id");

        builder.Property(e => e.ParentTicketId)
            .HasColumnName("parent_ticket_id");

        builder.Property(e => e.SiteId)
            .HasColumnName("site_id");

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

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.BatteryAssetId);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.Priority);
        builder.HasIndex(e => e.MergedIntoTicketId);
        // Lấy ticket con của một ticket cha, và gom ticket theo site — cả hai đều là truy vấn
        // theo cột đơn nên index đơn là đủ.
        builder.HasIndex(e => e.ParentTicketId);
        builder.HasIndex(e => e.SiteId);
        builder.HasIndex(e => new { e.Status, e.ScheduledStartAtUtc })
            .HasDatabaseName("ix_tickets_due_activation")
            .HasFilter("is_deleted = false AND status = 2 AND scheduled_start_at_utc IS NOT NULL");
        builder.HasIndex(e => new { e.BatteryAssetId, e.PeriodicMaintenanceDueAtUtc })
            .IsUnique()
            .HasDatabaseName("ux_tickets_periodic_maintenance_battery_due")
            .HasFilter("is_deleted = false AND periodic_maintenance_due_at_utc IS NOT NULL");
    }
}
