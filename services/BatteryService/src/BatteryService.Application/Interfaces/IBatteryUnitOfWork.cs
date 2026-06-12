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

    IGenericRepository<OutboxMessage> OutboxMessages { get; }

    // Sprint 5B #89 — Ambient monitoring.
    IGenericRepository<AmbientReading> AmbientReadings { get; }
    IGenericRepository<AmbientThresholdConfig> AmbientThresholdConfigs { get; }

    // Sprint 5B #100 — Environmental incident scope.
    IGenericRepository<EnvironmentalIncident> EnvironmentalIncidents { get; }

    // Sprint 5B B1 (#152) — Noise suppression hypertable.
    IGenericRepository<NoiseBreachEvent> NoiseBreachEvents { get; }
}
