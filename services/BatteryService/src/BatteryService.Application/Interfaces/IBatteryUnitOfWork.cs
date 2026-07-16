using BatteryService.Domain.Entities;
using SharedKernels.Interfaces;

namespace BatteryService.Application.Interfaces;

public interface IBatteryUnitOfWork : IUnitOfWork
{
    IGenericRepository<BatteryType> BatteryTypes { get; }

    IGenericRepository<BatteryAsset> BatteryAssets { get; }

    IGenericRepository<Site> Sites { get; }

    IGenericRepository<CustomerAccount> CustomerAccounts { get; }

    IGenericRepository<ThresholdConfig> ThresholdConfigs { get; }

    IGenericRepository<SensorReading> SensorReadings { get; }

    IGenericRepository<Alert> Alerts { get; }
    IGenericRepository<BatteryAuditLog> BatteryAuditLogs { get; }       // Sprint audit #AUDIT-20
    IGenericRepository<BatteryAuditOutbox> BatteryAuditOutboxes { get; } // Sprint audit #AUDIT-21

    IGenericRepository<OutboxMessage> OutboxMessages { get; }

    // Sprint 5B #89 — Ambient monitoring.
    IGenericRepository<AmbientReading> AmbientReadings { get; }
    IGenericRepository<AmbientThresholdConfig> AmbientThresholdConfigs { get; }

    // Sprint 5B #100 — Environmental incident scope.
    IGenericRepository<EnvironmentalIncident> EnvironmentalIncidents { get; }

    // Sprint 5B B1 (#152) — Noise suppression hypertable.
    IGenericRepository<NoiseBreachEvent> NoiseBreachEvents { get; }

    // Sprint Bonus NS-26 (#666, F2) — AI classification (spec §30.3).
    IGenericRepository<AnomalyClassification> AnomalyClassifications { get; }
    IGenericRepository<SohPrediction> SohPredictions { get; }

    // Sprint IoT-1 (#242) — IoT device management.
    IGenericRepository<IotDevice> IotDevices { get; }
    IGenericRepository<IotDeviceHeartbeat> IotDeviceHeartbeats { get; }
    IGenericRepository<IotDeviceCalibration> IotDeviceCalibrations { get; }
    IGenericRepository<IotFirmwareRelease> IotFirmwareReleases { get; }
    IGenericRepository<IotFirmwareUpdateLog> IotFirmwareUpdateLogs { get; }

    // Sprint IoT-2 #IoT2-16 — idempotency persistence.
    IGenericRepository<SensorIngestIdempotencyRecord> SensorIngestIdempotencyRecords { get; }
}
