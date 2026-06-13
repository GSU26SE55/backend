using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class SensorIngestIdempotencyRecordConfiguration : IEntityTypeConfiguration<SensorIngestIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<SensorIngestIdempotencyRecord> builder)
    {
        builder.ToTable("sensor_ingest_idempotency_records");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.DeviceCode).HasColumnName("device_code").HasMaxLength(64).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        builder.Property(r => r.Inserted).HasColumnName("inserted");
        builder.Property(r => r.Skipped).HasColumnName("skipped");
        builder.Property(r => r.TotalReceived).HasColumnName("total_received");
        builder.Property(r => r.Message).HasColumnName("message").HasMaxLength(500);
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(r => new { r.DeviceCode, r.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_sensor_ingest_idempotency_device_key");

        builder.HasIndex(r => r.ExpiresAt)
            .HasDatabaseName("idx_sensor_ingest_idempotency_expires_at");
    }
}
