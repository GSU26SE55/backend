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

        // Cột rút từ response AI. Bảng này KHÔNG có naming convention tự động — mọi cột
        // đều phải khai HasColumnName thủ công, nếu không EF lấy thẳng tên property và
        // sinh ra cột PascalCase giữa một bảng snake_case. Postgres sẽ bắt buộc phải quote
        // định danh đó mãi mãi ("HealthStage"), và mọi truy vấn tay đều dễ sai.
        builder.Property(p => p.HealthStage).HasColumnName("health_stage").HasMaxLength(32);
        builder.Property(p => p.StageConfidence).HasColumnName("stage_confidence").HasPrecision(4, 3);
        builder.Property(p => p.IsBorderline).HasColumnName("is_borderline").HasDefaultValue(false);
        builder.Property(p => p.SohStd).HasColumnName("soh_std").HasPrecision(5, 2);
        builder.Property(p => p.RulCyclesEstimate).HasColumnName("rul_cycles_estimate");
        builder.Property(p => p.AiPriority).HasColumnName("ai_priority").HasMaxLength(8);
        builder.Property(p => p.RiskLevel).HasColumnName("risk_level").HasMaxLength(16);
        builder.Property(p => p.ActionCode).HasColumnName("action_code").HasMaxLength(32);
        builder.Property(p => p.SohTrend).HasColumnName("soh_trend").HasMaxLength(16);
        builder.Property(p => p.DegradationRatePerCycle).HasColumnName("degradation_rate_per_cycle").HasPrecision(8, 5);
        builder.Property(p => p.CyclesToMaintenance).HasColumnName("cycles_to_maintenance");
        builder.Property(p => p.IsTemperatureOod).HasColumnName("is_temperature_ood").HasDefaultValue(false);

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
