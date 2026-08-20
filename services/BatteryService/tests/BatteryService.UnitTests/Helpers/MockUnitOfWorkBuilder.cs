using System.Linq.Expressions;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Helpers;

public sealed class MockUnitOfWorkBuilder
{
    public Mock<IBatteryUnitOfWork> UnitOfWork { get; } = new();
    public Mock<IGenericRepository<BatteryType>> BatteryTypes { get; } = new();
    public Mock<IGenericRepository<BatteryAsset>> BatteryAssets { get; } = new();
    public Mock<IGenericRepository<Site>> Sites { get; } = new();
    public Mock<IGenericRepository<CustomerAccount>> CustomerAccounts { get; } = new();
    public Mock<IGenericRepository<ThresholdConfig>> ThresholdConfigs { get; } = new();
    public Mock<IGenericRepository<SensorReading>> SensorReadings { get; } = new();
    public Mock<IGenericRepository<Alert>> Alerts { get; } = new();
    public Mock<IGenericRepository<OutboxMessage>> OutboxMessages { get; } = new();
    // GH-728 — nguồn replay audit.
    public Mock<IGenericRepository<BatteryAuditOutbox>> BatteryAuditOutboxes { get; } = new();

    // Sprint 5B additions.
    public Mock<IGenericRepository<AmbientReading>> AmbientReadings { get; } = new();
    public Mock<IGenericRepository<AmbientThresholdConfig>> AmbientThresholdConfigs { get; } = new();
    public Mock<IGenericRepository<EnvironmentalIncident>> EnvironmentalIncidents { get; } = new();
    public Mock<IGenericRepository<NoiseBreachEvent>> NoiseBreachEvents { get; } = new();

    // Sprint Bonus NS-26 — AI classification.
    public Mock<IGenericRepository<AnomalyClassification>> AnomalyClassifications { get; } = new();
    public Mock<IGenericRepository<SohPrediction>> SohPredictions { get; } = new();

    // Sprint IoT-1 additions.
    public Mock<IGenericRepository<IotDevice>> IotDevices { get; } = new();
    public Mock<IGenericRepository<IotDeviceHeartbeat>> IotDeviceHeartbeats { get; } = new();
    public Mock<IGenericRepository<IotDeviceCalibration>> IotDeviceCalibrations { get; } = new();
    public Mock<IGenericRepository<IotDeviceCommand>> IotDeviceCommands { get; } = new();
    public Mock<IGenericRepository<IotFirmwareRelease>> IotFirmwareReleases { get; } = new();
    public Mock<IGenericRepository<IotFirmwareUpdateLog>> IotFirmwareUpdateLogs { get; } = new();

    // Sprint IoT-2 #IoT2-16 — idempotency.
    public Mock<IGenericRepository<SensorIngestIdempotencyRecord>> SensorIngestIdempotencyRecords { get; } = new();

    // Import dữ liệu bên thứ ba.
    public Mock<IGenericRepository<ImportBatch>> ImportBatches { get; } = new();
    public Mock<IGenericRepository<ImportRow>> ImportRows { get; } = new();
    public Mock<IGenericRepository<ImportEntityLink>> ImportEntityLinks { get; } = new();

    public MockUnitOfWorkBuilder()
    {
        UnitOfWork.SetupGet(x => x.BatteryTypes).Returns(BatteryTypes.Object);
        UnitOfWork.SetupGet(x => x.BatteryAssets).Returns(BatteryAssets.Object);
        UnitOfWork.SetupGet(x => x.Sites).Returns(Sites.Object);
        UnitOfWork.SetupGet(x => x.CustomerAccounts).Returns(CustomerAccounts.Object);
        UnitOfWork.SetupGet(x => x.ThresholdConfigs).Returns(ThresholdConfigs.Object);
        UnitOfWork.SetupGet(x => x.SensorReadings).Returns(SensorReadings.Object);
        UnitOfWork.SetupGet(x => x.Alerts).Returns(Alerts.Object);
        UnitOfWork.SetupGet(x => x.OutboxMessages).Returns(OutboxMessages.Object);
        UnitOfWork.SetupGet(x => x.BatteryAuditOutboxes).Returns(BatteryAuditOutboxes.Object);
        UnitOfWork.SetupGet(x => x.AmbientReadings).Returns(AmbientReadings.Object);
        UnitOfWork.SetupGet(x => x.AmbientThresholdConfigs).Returns(AmbientThresholdConfigs.Object);
        UnitOfWork.SetupGet(x => x.EnvironmentalIncidents).Returns(EnvironmentalIncidents.Object);
        UnitOfWork.SetupGet(x => x.NoiseBreachEvents).Returns(NoiseBreachEvents.Object);
        UnitOfWork.SetupGet(x => x.AnomalyClassifications).Returns(AnomalyClassifications.Object);
        UnitOfWork.SetupGet(x => x.SohPredictions).Returns(SohPredictions.Object);
        UnitOfWork.SetupGet(x => x.IotDevices).Returns(IotDevices.Object);
        UnitOfWork.SetupGet(x => x.IotDeviceHeartbeats).Returns(IotDeviceHeartbeats.Object);
        UnitOfWork.SetupGet(x => x.IotDeviceCalibrations).Returns(IotDeviceCalibrations.Object);
        UnitOfWork.SetupGet(x => x.IotDeviceCommands).Returns(IotDeviceCommands.Object);
        UnitOfWork.SetupGet(x => x.IotFirmwareReleases).Returns(IotFirmwareReleases.Object);
        UnitOfWork.SetupGet(x => x.IotFirmwareUpdateLogs).Returns(IotFirmwareUpdateLogs.Object);
        UnitOfWork.SetupGet(x => x.SensorIngestIdempotencyRecords).Returns(SensorIngestIdempotencyRecords.Object);
        UnitOfWork.SetupGet(x => x.ImportBatches).Returns(ImportBatches.Object);
        UnitOfWork.SetupGet(x => x.ImportRows).Returns(ImportRows.Object);
        UnitOfWork.SetupGet(x => x.ImportEntityLinks).Returns(ImportEntityLinks.Object);
        UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        UnitOfWork.Setup(x => x.BeginTransactionAsync()).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.CommitTransactionAsync()).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        Seed(BatteryTypes, Array.Empty<BatteryType>());
        Seed(BatteryAssets, Array.Empty<BatteryAsset>());
        Seed(Sites, Array.Empty<Site>());
        Seed(CustomerAccounts, Array.Empty<CustomerAccount>());
        Seed(ThresholdConfigs, Array.Empty<ThresholdConfig>());
        Seed(SensorReadings, Array.Empty<SensorReading>());
        Seed(Alerts, Array.Empty<Alert>());
        Seed(OutboxMessages, Array.Empty<OutboxMessage>());
        Seed(BatteryAuditOutboxes, Array.Empty<BatteryAuditOutbox>());
        Seed(AmbientReadings, Array.Empty<AmbientReading>());
        Seed(AmbientThresholdConfigs, Array.Empty<AmbientThresholdConfig>());
        Seed(EnvironmentalIncidents, Array.Empty<EnvironmentalIncident>());
        Seed(NoiseBreachEvents, Array.Empty<NoiseBreachEvent>());
        Seed(AnomalyClassifications, Array.Empty<AnomalyClassification>());
        Seed(SohPredictions, Array.Empty<SohPrediction>());
        Seed(IotDevices, Array.Empty<IotDevice>());
        Seed(IotDeviceHeartbeats, Array.Empty<IotDeviceHeartbeat>());
        Seed(IotDeviceCalibrations, Array.Empty<IotDeviceCalibration>());
        Seed(IotDeviceCommands, Array.Empty<IotDeviceCommand>());
        Seed(IotFirmwareReleases, Array.Empty<IotFirmwareRelease>());
        Seed(IotFirmwareUpdateLogs, Array.Empty<IotFirmwareUpdateLog>());
        Seed(SensorIngestIdempotencyRecords, Array.Empty<SensorIngestIdempotencyRecord>());
        Seed(ImportBatches, Array.Empty<ImportBatch>());
        Seed(ImportRows, Array.Empty<ImportRow>());
        Seed(ImportEntityLinks, Array.Empty<ImportEntityLink>());
    }

    public MockUnitOfWorkBuilder WithIotDevices(params IotDevice[] data) { Seed(IotDevices, data); return this; }
    public MockUnitOfWorkBuilder WithIotFirmwareReleases(params IotFirmwareRelease[] data) { Seed(IotFirmwareReleases, data); return this; }
    public MockUnitOfWorkBuilder WithIotDeviceCalibrations(params IotDeviceCalibration[] data) { Seed(IotDeviceCalibrations, data); return this; }
    public MockUnitOfWorkBuilder WithIotDeviceCommands(params IotDeviceCommand[] data) { Seed(IotDeviceCommands, data); return this; }
    public MockUnitOfWorkBuilder WithIotFirmwareUpdateLogs(params IotFirmwareUpdateLog[] data) { Seed(IotFirmwareUpdateLogs, data); return this; }

    public MockUnitOfWorkBuilder WithBatteryTypes(params BatteryType[] data) { Seed(BatteryTypes, data); return this; }
    public MockUnitOfWorkBuilder WithBatteryAssets(params BatteryAsset[] data) { Seed(BatteryAssets, data); return this; }
    public MockUnitOfWorkBuilder WithSites(params Site[] data) { Seed(Sites, data); return this; }
    public MockUnitOfWorkBuilder WithCustomerAccounts(params CustomerAccount[] data) { Seed(CustomerAccounts, data); return this; }
    public MockUnitOfWorkBuilder WithThresholdConfigs(params ThresholdConfig[] data) { Seed(ThresholdConfigs, data); return this; }
    public MockUnitOfWorkBuilder WithSensorReadings(params SensorReading[] data) { Seed(SensorReadings, data); return this; }
    public MockUnitOfWorkBuilder WithAlerts(params Alert[] data) { Seed(Alerts, data); return this; }
    public MockUnitOfWorkBuilder WithOutboxMessages(params OutboxMessage[] data) { Seed(OutboxMessages, data); return this; }
    public MockUnitOfWorkBuilder WithBatteryAuditOutboxes(params BatteryAuditOutbox[] data) { Seed(BatteryAuditOutboxes, data); return this; }
    public MockUnitOfWorkBuilder WithAmbientReadings(params AmbientReading[] data) { Seed(AmbientReadings, data); return this; }
    public MockUnitOfWorkBuilder WithAmbientThresholdConfigs(params AmbientThresholdConfig[] data) { Seed(AmbientThresholdConfigs, data); return this; }
    public MockUnitOfWorkBuilder WithEnvironmentalIncidents(params EnvironmentalIncident[] data) { Seed(EnvironmentalIncidents, data); return this; }
    public MockUnitOfWorkBuilder WithNoiseBreachEvents(params NoiseBreachEvent[] data) { Seed(NoiseBreachEvents, data); return this; }
    public MockUnitOfWorkBuilder WithAnomalyClassifications(params AnomalyClassification[] data) { Seed(AnomalyClassifications, data); return this; }
    public MockUnitOfWorkBuilder WithSohPredictions(params SohPrediction[] data) { Seed(SohPredictions, data); return this; }

    public MockUnitOfWorkBuilder WithImportBatches(params ImportBatch[] data) { Seed(ImportBatches, data); return this; }
    public MockUnitOfWorkBuilder WithImportRows(params ImportRow[] data) { Seed(ImportRows, data); return this; }
    public MockUnitOfWorkBuilder WithImportEntityLinks(params ImportEntityLink[] data) { Seed(ImportEntityLinks, data); return this; }

    public IBatteryUnitOfWork Build() => UnitOfWork.Object;

    private static void Seed<T>(Mock<IGenericRepository<T>> repo, IEnumerable<T> data) where T : class
    {
        var list = data.ToList();
        repo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<T>(list));
        repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(() => new TestAsyncEnumerable<T>(list));
        repo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .Returns<Expression<Func<T, bool>>>(pred => new TestAsyncEnumerable<T>(list.AsQueryable().Where(pred)));
        repo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .Returns<Expression<Func<T, bool>>>(pred => Task.FromResult(list.AsQueryable().Any(pred)));
        repo.Setup(r => r.GetByIdAsync(It.IsAny<object>()))
            .Returns<object>(id => Task.FromResult(list.FirstOrDefault(e => Equals(GetIdValue(e), id))));
        repo.Setup(r => r.AddAsync(It.IsAny<T>())).Callback<T>(e => list.Add(e)).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<T>()));
        repo.Setup(r => r.DeleteAsync(It.IsAny<T>()));
    }

    private static object? GetIdValue<T>(T entity)
        => typeof(T).GetProperty("Id")?.GetValue(entity);
}
