using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class MaintenanceCycleConfiguration : IEntityTypeConfiguration<MaintenanceCycle>
{
    public void Configure(EntityTypeBuilder<MaintenanceCycle> builder)
    {
        builder.ToTable("maintenance_cycles");

        builder.HasKey(cycle => cycle.Id);

        builder.Property(cycle => cycle.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cycle => cycle.BatteryAssetId)
            .HasColumnName("battery_asset_id")
            .IsRequired();

        builder.Property(cycle => cycle.CycleNo)
            .HasColumnName("cycle_no")
            .IsRequired();

        builder.Property(cycle => cycle.DueAtUtc)
            .HasColumnName("due_at_utc")
            .IsRequired();

        builder.Property(cycle => cycle.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .IsRequired();

        builder.Property(cycle => cycle.SohPercentAtCycle)
            .HasColumnName("soh_percent_at_cycle")
            .HasPrecision(5, 2);

        // Không có khoá ngoại: ticket nằm ở service khác.
        builder.Property(cycle => cycle.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(cycle => cycle.AvgTemperatureCelsius)
            .HasColumnName("avg_temperature_celsius")
            .HasPrecision(5, 2);

        builder.Property(cycle => cycle.MaxTemperatureCelsius)
            .HasColumnName("max_temperature_celsius")
            .HasPrecision(5, 2);

        builder.Property(cycle => cycle.MinVoltage)
            .HasColumnName("min_voltage")
            .HasPrecision(6, 2);

        builder.Property(cycle => cycle.MaxVoltage)
            .HasColumnName("max_voltage")
            .HasPrecision(6, 2);

        builder.Property(cycle => cycle.CycleCountDelta)
            .HasColumnName("cycle_count_delta");

        builder.Property(cycle => cycle.AlertCount)
            .HasColumnName("alert_count");

        builder.Property(cycle => cycle.CriticalAlertCount)
            .HasColumnName("critical_alert_count");

        builder.Property(cycle => cycle.ReadingCount)
            .HasColumnName("reading_count");

        builder.Property(cycle => cycle.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(cycle => cycle.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(cycle => cycle.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(cycle => cycle.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(cycle => cycle.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasOne(cycle => cycle.BatteryAsset)
            .WithMany(asset => asset.MaintenanceCycles)
            .HasForeignKey(cycle => cycle.BatteryAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Một kỳ chỉ được ghi đúng một lần cho mỗi pin. Consumer nhận event trùng (retry,
        // redelivery) sẽ va vào unique index này thay vì tạo bản ghi thứ hai.
        builder.HasIndex(cycle => new { cycle.BatteryAssetId, cycle.CycleNo })
            .IsUnique()
            .HasDatabaseName("ux_maintenance_cycles_asset_cycle_no")
            .HasFilter("is_deleted = false");

        // Tab lịch sử đọc theo pin, mới nhất trước.
        builder.HasIndex(cycle => new { cycle.BatteryAssetId, cycle.DueAtUtc })
            .HasDatabaseName("ix_maintenance_cycles_asset_due");

        // Truy ngược "ticket này thuộc kỳ nào", và lọc nhanh các kỳ chưa gắn ticket khi
        // backfill. Partial index: phần lớn giá trị sẽ non-null, nhưng các dòng null là
        // thứ backfill phải quét nên vẫn đáng đánh chỉ mục riêng.
        builder.HasIndex(cycle => cycle.TicketId)
            .HasDatabaseName("ix_maintenance_cycles_ticket")
            .HasFilter("ticket_id IS NOT NULL");

        builder.Ignore(cycle => cycle.DomainEvents);
    }
}
