using BatteryService.Application.Common.Models;
using BatteryService.Application.CQRS.Handler.Maintenance;
using BatteryService.Application.CQRS.Query.Maintenance;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// Luồng nhật ký bảo trì định kỳ chạy qua DbContext và UnitOfWork thật: worker ghi mốc →
/// query đọc lại đúng thứ tự và đúng chỉ số.
/// </summary>
public class MaintenanceCycleFlowIntegrationTests
{
    private static readonly DateTime PeriodStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DueAt = new(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NowUtc = new(2027, 3, 1, 0, 5, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecordDueCycles_ThenQuery_ReturnsCycleWithSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var assetId = await SeedAssetAsync(dbContext);
        await SeedReadingsAsync(dbContext, assetId);
        await SeedAlertsAsync(dbContext, assetId);

        var service = CreateService(dbContext);
        var recorded = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);
        recorded.Should().Be(1);

        // Đọc lại qua chính query handler mà controller dùng.
        var handler = new GetMaintenanceCyclesQueryHandler(new UnitOfWork(dbContext));
        var response = await handler.Handle(
            new GetMaintenanceCyclesQuery { BatteryAssetId = assetId }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);

        var cycle = response.Data.Should().ContainSingle().Subject;
        cycle.CycleNo.Should().Be(1);
        cycle.DueAtUtc.Should().Be(DueAt);
        cycle.RecordedAtUtc.Should().Be(NowUtc);

        // SoH lấy từ bản ghi MỚI NHẤT trong kỳ, không phải trung bình.
        cycle.SohPercentAtCycle.Should().Be(90m);
        cycle.AvgTemperatureCelsius.Should().Be(40m);
        cycle.MaxTemperatureCelsius.Should().Be(50m);
        cycle.MinVoltage.Should().Be(25m);
        cycle.MaxVoltage.Should().Be(28m);
        cycle.CycleCountDelta.Should().Be(50);
        // Bản ghi và cảnh báo ngoài khoảng kỳ không được tính vào.
        cycle.ReadingCount.Should().Be(3);
        cycle.AlertCount.Should().Be(2);
        cycle.CriticalAlertCount.Should().Be(1);

        // Lịch đã dời sang kỳ kế tiếp.
        var asset = await dbContext.BatteryAssets.SingleAsync(a => a.Id == assetId);
        asset.LastMaintenanceAtUtc.Should().Be(DueAt);
        asset.NextMaintenanceDueAtUtc.Should().Be(DueAt.AddMonths(6));
        asset.MaintenanceCycleNo.Should().Be(2);
    }

    [Fact]
    public async Task RecordDueCycles_RunTwice_DoesNotDuplicateCycle()
    {
        await using var dbContext = CreateDbContext();
        var assetId = await SeedAssetAsync(dbContext);
        var service = CreateService(dbContext);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);
        // Tick thứ hai: lịch đã dời sang 6 tháng sau nên không còn gì tới hạn.
        var second = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        second.Should().Be(0);
        var cycles = await dbContext.MaintenanceCycles.ToListAsync();
        cycles.Should().ContainSingle();
    }

    [Fact]
    public async Task Query_ReturnsCyclesNewestFirst()
    {
        await using var dbContext = CreateDbContext();
        var assetId = await SeedAssetAsync(dbContext);

        // Ghi ngược thứ tự để chắc query sắp xếp chứ không dựa vào thứ tự chèn.
        foreach (var cycleNo in new[] { 2, 1, 3 })
        {
            dbContext.MaintenanceCycles.Add(new MaintenanceCycle
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assetId,
                CycleNo = cycleNo,
                DueAtUtc = DueAt.AddMonths(6 * cycleNo),
                RecordedAtUtc = NowUtc,
                CreatedAt = NowUtc
            });
        }
        await dbContext.SaveChangesAsync();

        var handler = new GetMaintenanceCyclesQueryHandler(new UnitOfWork(dbContext));
        var response = await handler.Handle(
            new GetMaintenanceCyclesQuery { BatteryAssetId = assetId }, CancellationToken.None);

        response.Data!.Select(c => c.CycleNo).Should().ContainInOrder(3, 2, 1);
    }

    [Fact]
    public async Task Query_ForBatteryWithNoCycles_ReturnsEmptyNotError()
    {
        await using var dbContext = CreateDbContext();
        var handler = new GetMaintenanceCyclesQueryHandler(new UnitOfWork(dbContext));

        var response = await handler.Handle(
            new GetMaintenanceCyclesQuery { BatteryAssetId = Guid.NewGuid() },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_ExcludesSoftDeletedCycles()
    {
        await using var dbContext = CreateDbContext();
        var assetId = await SeedAssetAsync(dbContext);

        dbContext.MaintenanceCycles.AddRange(
            new MaintenanceCycle
            {
                Id = Guid.NewGuid(), BatteryAssetId = assetId, CycleNo = 1,
                DueAtUtc = DueAt, RecordedAtUtc = NowUtc, CreatedAt = NowUtc
            },
            new MaintenanceCycle
            {
                Id = Guid.NewGuid(), BatteryAssetId = assetId, CycleNo = 2,
                DueAtUtc = DueAt.AddMonths(6), RecordedAtUtc = NowUtc, CreatedAt = NowUtc,
                IsDeleted = true
            });
        await dbContext.SaveChangesAsync();

        var handler = new GetMaintenanceCyclesQueryHandler(new UnitOfWork(dbContext));
        var response = await handler.Handle(
            new GetMaintenanceCyclesQuery { BatteryAssetId = assetId }, CancellationToken.None);

        response.Data.Should().ContainSingle();
        response.Data![0].CycleNo.Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MaintenanceScheduleService CreateService(ApplicationDbContext dbContext) =>
        new(new UnitOfWork(dbContext),
            Microsoft.Extensions.Options.Options.Create(new MaintenanceScheduleOptions
            {
                Enabled = true,
                DefaultCycleMonths = 6,
                BatchSize = 100
            }),
            Mock.Of<ILogger<MaintenanceScheduleService>>());

    private static async Task<Guid> SeedAssetAsync(ApplicationDbContext dbContext)
    {
        var typeId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        dbContext.BatteryTypes.Add(new BatteryType
        {
            Id = typeId,
            Name = "Integration type",
            NominalCapacityAh = 100m,
            NominalVoltage = 24m
        });
        dbContext.BatteryAssets.Add(new BatteryAsset
        {
            Id = assetId,
            SerialNumber = $"BAT-INT-{assetId:N}"[..20],
            BatteryTypeId = typeId,
            CustomerId = Guid.NewGuid(),
            InstallDate = PeriodStart.AddMonths(-6),
            Status = BatteryStatusEnum.Active,
            LastMaintenanceAtUtc = PeriodStart,
            NextMaintenanceDueAtUtc = DueAt,
            MaintenanceCycleNo = 1
        });
        await dbContext.SaveChangesAsync();
        return assetId;
    }

    private static async Task SeedReadingsAsync(ApplicationDbContext dbContext, Guid assetId)
    {
        SensorReading Reading(DateTime at, decimal temp, decimal volt, int cycles, decimal soh) => new()
        {
            BatteryAssetId = assetId,
            Time = at,
            Temperature = temp,
            Voltage = volt,
            Current = 2m,
            SocPercent = 60m,
            CycleCount = cycles,
            SohPercent = soh
        };

        dbContext.SensorReadings.AddRange(
            Reading(PeriodStart.AddDays(1), 30m, 26m, 100, 92m),
            Reading(PeriodStart.AddDays(60), 40m, 25m, 120, 91m),
            Reading(DueAt.AddDays(-1), 50m, 28m, 150, 90m),
            // Ngoài khoảng kỳ — phải bị loại.
            Reading(DueAt.AddDays(5), 99m, 30m, 999, 10m),
            Reading(PeriodStart.AddDays(-5), 5m, 20m, 1, 99m));
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedAlertsAsync(ApplicationDbContext dbContext, Guid assetId)
    {
        dbContext.Alerts.AddRange(
            new Alert
            {
                Id = Guid.NewGuid(), BatteryAssetId = assetId,
                DetectedAt = PeriodStart.AddDays(2), Severity = AlertSeverityEnum.Warning
            },
            new Alert
            {
                Id = Guid.NewGuid(), BatteryAssetId = assetId,
                DetectedAt = PeriodStart.AddDays(3), Severity = AlertSeverityEnum.Critical
            },
            // Ngoài khoảng kỳ — phải bị loại.
            new Alert
            {
                Id = Guid.NewGuid(), BatteryAssetId = assetId,
                DetectedAt = DueAt.AddDays(10), Severity = AlertSeverityEnum.Critical
            });
        await dbContext.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-cycle-integration-{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new CurrentUserService(new HttpContextAccessor());
        var interceptor = new AuditableEntityInterceptor(currentUser);

        return new ApplicationDbContext(options, interceptor);
    }
}
