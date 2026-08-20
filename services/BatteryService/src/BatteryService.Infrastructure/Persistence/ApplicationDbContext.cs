using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;

namespace BatteryService.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    private readonly AuditableEntityInterceptor? _auditableEntityInterceptor;

    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntityInterceptor auditableEntityInterceptor) : base(options)
    {
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public virtual DbSet<BatteryType> BatteryTypes { get; set; }

    public virtual DbSet<BatteryAsset> BatteryAssets { get; set; }

    public virtual DbSet<Site> Sites { get; set; }

    public virtual DbSet<CustomerAccount> CustomerAccounts { get; set; }

    public virtual DbSet<ThresholdConfig> ThresholdConfigs { get; set; }

    public virtual DbSet<SensorReading> SensorReadings { get; set; }

    public virtual DbSet<Alert> Alerts { get; set; }

    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<BatteryAuditLog> BatteryAuditLogs { get; set; }       // Sprint audit #AUDIT-20
    public virtual DbSet<BatteryAuditOutbox> BatteryAuditOutboxes { get; set; } // Sprint audit #AUDIT-21

    // Sprint 5B #89 — Ambient monitoring.
    public virtual DbSet<AmbientReading> AmbientReadings { get; set; }
    public virtual DbSet<AmbientThresholdConfig> AmbientThresholdConfigs { get; set; }

    // Sprint 5B #100 — Environmental incident.
    public virtual DbSet<EnvironmentalIncident> EnvironmentalIncidents { get; set; }

    // Sprint 5B B1 (#152) — Noise breach hypertable.
    public virtual DbSet<NoiseBreachEvent> NoiseBreachEvents { get; set; }

    // Sprint Bonus NS-26 (#666, F2) — AI classification (spec §30.3).
    public virtual DbSet<AnomalyClassification> AnomalyClassifications { get; set; }
    public virtual DbSet<SohPrediction> SohPredictions { get; set; }

    // Sprint IoT-1 (#242) — IoT edge device management.
    public virtual DbSet<IotDevice> IotDevices { get; set; }
    public virtual DbSet<IotDeviceHeartbeat> IotDeviceHeartbeats { get; set; }
    public virtual DbSet<IotDeviceCalibration> IotDeviceCalibrations { get; set; }
    public virtual DbSet<IotDeviceCommand> IotDeviceCommands { get; set; }
    public virtual DbSet<IotFirmwareRelease> IotFirmwareReleases { get; set; }
    public virtual DbSet<IotFirmwareUpdateLog> IotFirmwareUpdateLogs { get; set; }

    // Sprint IoT-2 #IoT2-16 (S3-BE-03) — idempotency persistence cho sensor batch ingest.
    public virtual DbSet<SensorIngestIdempotencyRecord> SensorIngestIdempotencyRecords { get; set; }

    // Import dữ liệu bên thứ ba
    public virtual DbSet<ImportBatch> ImportBatches { get; set; }
    public virtual DbSet<ImportRow> ImportRows { get; set; }
    public virtual DbSet<ImportEntityLink> ImportEntityLinks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_auditableEntityInterceptor is not null)
            optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
