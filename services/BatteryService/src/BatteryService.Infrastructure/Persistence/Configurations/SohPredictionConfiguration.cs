using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint Bonus NS-26 (#666, F2) — spec §30.3.</summary>
public class SohPredictionConfiguration : IEntityTypeConfiguration<SohPrediction>
{
    public void Configure(EntityTypeBuilder<SohPrediction> builder)
    {
        builder.ToTable("soh_predictions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.BatteryAssetId).HasColumnName("battery_asset_id").IsRequired();
        builder.Property(p => p.PredictedSohPercent).HasColumnName("predicted_soh_percent").HasPrecision(5, 2);
        builder.Property(p => p.Confidence).HasColumnName("confidence").HasPrecision(4, 3);
        builder.Property(p => p.ModelVersion).HasColumnName("model_version").HasMaxLength(20);
        builder.Property(p => p.InputWindowStartUtc).HasColumnName("input_window_start_utc");
        builder.Property(p => p.InputWindowEndUtc).HasColumnName("input_window_end_utc");
        builder.Property(p => p.PredictedAt).HasColumnName("predicted_at").IsRequired();
        builder.Property(p => p.LatencyMs).HasColumnName("latency_ms");
        builder.Property(p => p.RawResponse).HasColumnName("raw_response").HasColumnType("jsonb");

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(p => p.BatteryAsset)
            .WithMany()
            .HasForeignKey(p => p.BatteryAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lấy prediction mới nhất per asset (§30.3 PredictedAt indexed DESC).
        builder.HasIndex(p => new { p.BatteryAssetId, p.PredictedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_soh_predictions_asset_predicted_at");

        builder.Ignore(p => p.DomainEvents);
    }
}
