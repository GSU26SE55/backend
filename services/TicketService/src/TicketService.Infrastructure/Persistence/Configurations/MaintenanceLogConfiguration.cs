using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class MaintenanceLogConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> builder)
    {
        builder.ToTable("maintenance_logs");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.StaffId)
            .HasColumnName("staff_id");

        builder.Property(e => e.LogType)
            .HasColumnName("log_type")
            .HasConversion<int>();

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(500);

        builder.Property(e => e.DiagnosisDetails)
            .HasColumnName("diagnosis_details");

        builder.Property(e => e.ActionsTaken)
            .HasColumnName("actions_taken");

        builder.Property(e => e.DurationMinutes)
            .HasColumnName("duration_minutes");

        builder.Property(e => e.ResolutionNote)
            .HasColumnName("resolution_note");

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at");

        builder.Property(e => e.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(e => e.PartsUsed)
            .HasColumnName("parts_used")
            .HasColumnType("jsonb");

        builder.Property(e => e.AttachmentFileIds)
            .HasColumnName("attachment_file_ids")
            .HasColumnType("jsonb");

        builder.Property(e => e.BeforePhotosFileIds)
            .HasColumnName("before_photos_file_ids")
            .HasColumnType("jsonb");

        builder.Property(e => e.AfterPhotosFileIds)
            .HasColumnName("after_photos_file_ids")
            .HasColumnType("jsonb");

        builder.Property(e => e.RelatedKbArticleIds)
            .HasColumnName("related_kb_article_ids")
            .HasColumnType("jsonb");

        builder.Property(e => e.CheckInLatitude)
            .HasColumnName("check_in_latitude");

        builder.Property(e => e.CheckInLongitude)
            .HasColumnName("check_in_longitude");

        builder.Property(e => e.CheckInAt)
            .HasColumnName("check_in_at");

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

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.StaffId);
        builder.HasIndex(e => e.LogType);

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.MaintenanceLogs)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
