using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint Bonus NS-26 (#666, F2) — spec §30.3.</summary>
public class AnomalyClassificationConfiguration : IEntityTypeConfiguration<AnomalyClassification>
{
    public void Configure(EntityTypeBuilder<AnomalyClassification> builder)
    {
        builder.ToTable("anomaly_classifications");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.AlertId).HasColumnName("alert_id");
        builder.Property(c => c.BatteryAssetId).HasColumnName("battery_asset_id").IsRequired();

        builder.Property(c => c.Classification).HasColumnName("classification").HasConversion<int>();
        builder.Property(c => c.AnomalyScore).HasColumnName("anomaly_score").HasPrecision(8, 6);
        builder.Property(c => c.Confidence).HasColumnName("confidence").HasPrecision(4, 3);
        builder.Property(c => c.ModelVersion).HasColumnName("model_version").HasMaxLength(20);
        builder.Property(c => c.ClassifiedAt).HasColumnName("classified_at").IsRequired();
        builder.Property(c => c.LatencyMs).HasColumnName("latency_ms");

        builder.Property(c => c.StaffFeedback).HasColumnName("staff_feedback").HasConversion<int?>();
        builder.Property(c => c.StaffFeedbackByUserId).HasColumnName("staff_feedback_by_user_id");
        builder.Property(c => c.StaffFeedbackAt).HasColumnName("staff_feedback_at");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(c => c.Alert)
            .WithMany()
            .HasForeignKey(c => c.AlertId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.BatteryAsset)
            .WithMany()
            .HasForeignKey(c => c.BatteryAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.BatteryAssetId, c.ClassifiedAt })
            .HasDatabaseName("ix_anomaly_classifications_asset_classified_at");
        builder.HasIndex(c => c.AlertId).HasDatabaseName("ix_anomaly_classifications_alert_id");

        builder.Ignore(c => c.DomainEvents);
    }
}
