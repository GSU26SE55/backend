using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Infrastructure.Sagas;

namespace TicketService.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping cho <see cref="AlertTicketSagaState"/>.
/// Optimistic concurrency dùng PostgreSQL system column <c>xmin</c>.
///
/// Sprint 5B #237.
/// </summary>
public class AlertTicketSagaStateConfiguration : IEntityTypeConfiguration<AlertTicketSagaState>
{
    public void Configure(EntityTypeBuilder<AlertTicketSagaState> builder)
    {
        builder.ToTable("alert_ticket_saga_states");

        builder.HasKey(s => s.CorrelationId);

        builder.Property(s => s.CorrelationId)
            .HasColumnName("correlation_id")
            .ValueGeneratedNever();

        builder.Property(s => s.CurrentState)
            .HasColumnName("current_state")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.Version)
            .HasColumnName("version");

        builder.Property(s => s.AlertId).HasColumnName("alert_id").IsRequired();
        builder.Property(s => s.BatteryAssetId).HasColumnName("battery_asset_id");
        builder.Property(s => s.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(s => s.SiteId).HasColumnName("site_id");
        builder.Property(s => s.AssetSerialNumber).HasColumnName("asset_serial_number").HasMaxLength(100);
        builder.Property(s => s.AnomalyType).HasColumnName("anomaly_type").IsRequired();
        builder.Property(s => s.Severity).HasColumnName("severity").IsRequired();
        builder.Property(s => s.ThresholdValue).HasColumnName("threshold_value").HasPrecision(18, 6);
        builder.Property(s => s.ActualValue).HasColumnName("actual_value").HasPrecision(18, 6);
        builder.Property(s => s.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        builder.Property(s => s.DetectedAt).HasColumnName("detected_at").IsRequired();

        builder.Property(s => s.InternalResistanceMilliohm).HasColumnName("internal_resistance_milliohm").HasPrecision(10, 3);
        builder.Property(s => s.CellVoltageDeltaMv).HasColumnName("cell_voltage_delta_mv").HasPrecision(10, 3);
        builder.Property(s => s.EnvironmentalIncidentId).HasColumnName("environmental_incident_id");
        builder.Property(s => s.AiPrescription).HasColumnName("ai_prescription");  // BE-AI (nullable)

        builder.Property(s => s.TicketId).HasColumnName("ticket_id");
        builder.Property(s => s.TicketCode).HasColumnName("ticket_code").HasMaxLength(50);
        builder.Property(s => s.TicketIsReused).HasColumnName("ticket_is_reused").HasDefaultValue(false);

        builder.Property(s => s.FailedAtStage).HasColumnName("failed_at_stage").HasMaxLength(64);
        builder.Property(s => s.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(s => s.FailureErrorCode).HasColumnName("failure_error_code").HasMaxLength(50);
        builder.Property(s => s.FailedAt).HasColumnName("failed_at");
        builder.Property(s => s.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(s => s.CompletedAt).HasColumnName("completed_at");

        builder.Property(s => s.PendingTimeoutTokenId).HasColumnName("pending_timeout_token_id");

        // Lookup theo state cho admin query (active vs terminal).
        builder.HasIndex(s => s.CurrentState).HasDatabaseName("ix_alert_ticket_saga_states_current_state");
        builder.HasIndex(s => s.AlertId).IsUnique().HasDatabaseName("ux_alert_ticket_saga_states_alert_id");
        builder.HasIndex(s => s.TicketId).HasDatabaseName("ix_alert_ticket_saga_states_ticket_id");
        builder.HasIndex(s => s.BatteryAssetId).HasDatabaseName("ix_alert_ticket_saga_states_battery_asset_id");
        builder.HasIndex(s => s.StartedAt).HasDatabaseName("ix_alert_ticket_saga_states_started_at");
    }
}
