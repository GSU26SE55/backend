using BatteryService.Domain.Entities;
using SharedKernels.Interfaces;

namespace BatteryService.Application.Interfaces;

public interface IBatteryUnitOfWork : IUnitOfWork
{
    IGenericRepository<BatteryType> BatteryTypes { get; }

    IGenericRepository<BatteryAsset> BatteryAssets { get; }

    IGenericRepository<Site> Sites { get; }

    IGenericRepository<BatteryGroup> BatteryGroups { get; }

    IGenericRepository<CustomerAccount> CustomerAccounts { get; }

    IGenericRepository<ThresholdConfig> ThresholdConfigs { get; }

    IGenericRepository<SensorReading> SensorReadings { get; }

    IGenericRepository<Alert> Alerts { get; }

    IGenericRepository<OutboxMessage> OutboxMessages { get; }
}
