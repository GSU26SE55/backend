using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class EnvironmentalIncidentConfiguration : IEntityTypeConfiguration<EnvironmentalIncident>
{
    public void Configure(EntityTypeBuilder<EnvironmentalIncident> builder)
    {
        builder.ToTable("environmental_incidents");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.SiteId).HasColumnName("site_id").IsRequired();

        builder.Property(i => i.IncidentType)
            .HasColumnName("incident_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(EnvironmentalIncidentStatusEnum.Open)
            .IsRequired();

        builder.Property(i => i.Severity)
            .HasColumnName("severity")
            .HasConversion<int>()
            .HasDefaultValue(AlertSeverityEnum.Critical)
            .IsRequired();

        builder.Property(i => i.ReportedBy).HasColumnName("reported_by").HasMaxLength(150);
        builder.Property(i => i.DetectedAt).HasColumnName("detected_at").IsRequired();

        builder.Property(i => i.AcknowledgedBy).HasColumnName("acknowledged_by");
        builder.Property(i => i.AcknowledgedAt).HasColumnName("acknowledged_at");

        builder.Property(i => i.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(i => i.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(i => i.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(2000);

        builder.Property(i => i.FalseAlarmBy).HasColumnName("false_alarm_by");
        builder.Property(i => i.FalseAlarmAt).HasColumnName("false_alarm_at");
        builder.Property(i => i.FalseAlarmReason).HasColumnName("false_alarm_reason").HasMaxLength(500);

        builder.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.CreatedBy).HasColumnName("created_by");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(i => i.Site)
            .WithMany()
            .HasForeignKey(i => i.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Alerts)
            .WithOne(a => a.EnvironmentalIncident)
            .HasForeignKey(a => a.EnvironmentalIncidentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => new { i.SiteId, i.Status }).HasDatabaseName("ix_environmental_incidents_site_status");
        builder.HasIndex(i => i.DetectedAt).HasDatabaseName("ix_environmental_incidents_detected_at");

        builder.Ignore(i => i.DomainEvents);
    }
}
