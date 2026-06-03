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

        builder.Property(e => e.AssignedStaffId)
            .HasColumnName("assigned_staff_id");

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
        builder.HasIndex(e => e.AssignedStaffId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.Priority);
    }
}
