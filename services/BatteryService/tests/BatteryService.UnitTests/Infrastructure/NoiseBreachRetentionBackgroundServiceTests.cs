using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// Sprint 5B B1 (#152) — NoiseBreachRetention dọn dẹp rows > 7 ngày.
/// Dùng SQLite in-memory vì <c>ExecuteDeleteAsync</c> là relational-only operation.
/// </summary>
public class NoiseBreachRetentionBackgroundServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public NoiseBreachRetentionBackgroundServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        var http = new HttpContextAccessor();
        var interceptor = new AuditableEntityInterceptor(new CurrentUserService(http));
        var db = new ApplicationDbContext(options, interceptor);
        db.Database.EnsureCreated();
        return db;
    }

    private IServiceProvider BuildProvider()
    {
        var sc = new ServiceCollection();
        sc.AddScoped(_ => CreateDb());
        sc.AddScoped<BatteryService.Application.Interfaces.IBatteryUnitOfWork>(sp =>
            new UnitOfWork(sp.GetRequiredService<ApplicationDbContext>()));
        return sc.BuildServiceProvider();
    }

    private async Task<Guid> SeedAssetAsync()
    {
        using var seed = CreateDb();
        var batteryTypeId = Guid.NewGuid();
        seed.BatteryTypes.Add(new BatteryType
        {
            Id = batteryTypeId,
            Name = "TestType",
            Manufacturer = "Test",
            NominalVoltage = 12m,
            NominalCapacityAh = 100m,
            CreatedAt = DateTime.UtcNow
        });
        var assetId = Guid.NewGuid();
        seed.BatteryAssets.Add(new BatteryAsset
        {
            Id = assetId,
            SerialNumber = $"S-{Guid.NewGuid():N}".Substring(0, 16),
            BatteryTypeId = batteryTypeId,
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow.AddYears(-1),
            Status = BatteryStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return assetId;
    }

    [Fact]
    public async Task Service_ShouldPurgeRowsOlderThan7Days_KeepRecent()
    {
        // Seed
        var assetId = await SeedAssetAsync();
        using (var seed = CreateDb())
        {
            seed.NoiseBreachEvents.AddRange(
                new NoiseBreachEvent
                {
                    Time = DateTime.UtcNow.AddDays(-8),
                    BatteryAssetId = assetId,
                    AnomalyType = AnomalyTypeEnum.Undervoltage,
                    ThresholdValue = 10,
                    ActualValue = 9,
                    Unit = "V"
                },
                new NoiseBreachEvent
                {
                    Time = DateTime.UtcNow.AddDays(-1),
                    BatteryAssetId = assetId,
                    AnomalyType = AnomalyTypeEnum.Overheat,
                    ThresholdValue = 60,
                    ActualValue = 65,
                    Unit = "C"
                });
            await seed.SaveChangesAsync();
        }

        var provider = BuildProvider();
        var sut = new NoiseBreachRetentionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NoiseBreachRetentionBackgroundService>.Instance);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(500);
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(default);

        using (var verify = CreateDb())
        {
            var remaining = await verify.NoiseBreachEvents.ToListAsync();
            remaining.Should().ContainSingle();
            remaining[0].Time.Should().BeAfter(DateTime.UtcNow.AddDays(-7));
        }
    }

    [Fact]
    public async Task Service_EmptyTable_ShouldNotThrow()
    {
        using (var ensure = CreateDb())
        { /* create schema */ }

        var provider = BuildProvider();
        var sut = new NoiseBreachRetentionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NoiseBreachRetentionBackgroundService>.Instance);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(default);

        using var verify = CreateDb();
        var count = await verify.NoiseBreachEvents.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Service_AllRecent_ShouldKeepAll()
    {
        var assetId = await SeedAssetAsync();
        using (var seed = CreateDb())
        {
            seed.NoiseBreachEvents.AddRange(
                new NoiseBreachEvent { Time = DateTime.UtcNow.AddDays(-1), BatteryAssetId = assetId, AnomalyType = AnomalyTypeEnum.Overheat, ThresholdValue = 60, ActualValue = 65, Unit = "C" },
                new NoiseBreachEvent { Time = DateTime.UtcNow.AddDays(-3), BatteryAssetId = assetId, AnomalyType = AnomalyTypeEnum.Overvoltage, ThresholdValue = 14, ActualValue = 15, Unit = "V" }
            );
            await seed.SaveChangesAsync();
        }

        var provider = BuildProvider();
        var sut = new NoiseBreachRetentionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NoiseBreachRetentionBackgroundService>.Instance);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(default);

        using var verify = CreateDb();
        (await verify.NoiseBreachEvents.CountAsync()).Should().Be(2);
    }
}
