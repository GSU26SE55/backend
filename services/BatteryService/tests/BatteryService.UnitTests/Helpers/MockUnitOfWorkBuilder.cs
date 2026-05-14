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
    public Mock<IGenericRepository<BatteryGroup>> BatteryGroups { get; } = new();
    public Mock<IGenericRepository<CustomerAccount>> CustomerAccounts { get; } = new();
    public Mock<IGenericRepository<ThresholdConfig>> ThresholdConfigs { get; } = new();
    public Mock<IGenericRepository<SensorReading>> SensorReadings { get; } = new();
    public Mock<IGenericRepository<Alert>> Alerts { get; } = new();

    public MockUnitOfWorkBuilder()
    {
        UnitOfWork.SetupGet(x => x.BatteryTypes).Returns(BatteryTypes.Object);
        UnitOfWork.SetupGet(x => x.BatteryAssets).Returns(BatteryAssets.Object);
        UnitOfWork.SetupGet(x => x.Sites).Returns(Sites.Object);
        UnitOfWork.SetupGet(x => x.BatteryGroups).Returns(BatteryGroups.Object);
        UnitOfWork.SetupGet(x => x.CustomerAccounts).Returns(CustomerAccounts.Object);
        UnitOfWork.SetupGet(x => x.ThresholdConfigs).Returns(ThresholdConfigs.Object);
        UnitOfWork.SetupGet(x => x.SensorReadings).Returns(SensorReadings.Object);
        UnitOfWork.SetupGet(x => x.Alerts).Returns(Alerts.Object);
        UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        UnitOfWork.Setup(x => x.BeginTransactionAsync()).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.CommitTransactionAsync()).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        Seed(BatteryTypes, Array.Empty<BatteryType>());
        Seed(BatteryAssets, Array.Empty<BatteryAsset>());
        Seed(Sites, Array.Empty<Site>());
        Seed(BatteryGroups, Array.Empty<BatteryGroup>());
        Seed(CustomerAccounts, Array.Empty<CustomerAccount>());
        Seed(ThresholdConfigs, Array.Empty<ThresholdConfig>());
        Seed(SensorReadings, Array.Empty<SensorReading>());
        Seed(Alerts, Array.Empty<Alert>());
    }

    public MockUnitOfWorkBuilder WithBatteryTypes(params BatteryType[] data) { Seed(BatteryTypes, data); return this; }
    public MockUnitOfWorkBuilder WithBatteryAssets(params BatteryAsset[] data) { Seed(BatteryAssets, data); return this; }
    public MockUnitOfWorkBuilder WithSites(params Site[] data) { Seed(Sites, data); return this; }
    public MockUnitOfWorkBuilder WithBatteryGroups(params BatteryGroup[] data) { Seed(BatteryGroups, data); return this; }
    public MockUnitOfWorkBuilder WithCustomerAccounts(params CustomerAccount[] data) { Seed(CustomerAccounts, data); return this; }
    public MockUnitOfWorkBuilder WithThresholdConfigs(params ThresholdConfig[] data) { Seed(ThresholdConfigs, data); return this; }
    public MockUnitOfWorkBuilder WithSensorReadings(params SensorReading[] data) { Seed(SensorReadings, data); return this; }
    public MockUnitOfWorkBuilder WithAlerts(params Alert[] data) { Seed(Alerts, data); return this; }

    public IBatteryUnitOfWork Build() => UnitOfWork.Object;

    private static void Seed<T>(Mock<IGenericRepository<T>> repo, IEnumerable<T> data) where T : class
    {
        var list = data.ToList();
        repo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<T>(list));
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
