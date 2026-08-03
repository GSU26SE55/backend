using System.Text.Json;
using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// End-to-end flow: SensorReading → AnomalyDetectionService.ScanRecentReadings → Alert + Outbox →
/// OutboxRelayService.RelayBatch → IMessageProducerService.PublishAsync. Test với InMemory DB.
/// </summary>
public class Sprint3AnomalyFlowIntegrationTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatteryTypeId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();

    [Fact]
    public async Task EndToEnd_CriticalAnomaly_CreatesAlertAndOutboxEvent()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.SensorReadings.Add(new SensorReading
        {
            Time = DateTime.UtcNow.AddSeconds(-10),
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 65,
            SocPercent = 60
        });
        await db.SaveChangesAsync();

        var svc = new AnomalyDetectionService(
            new UnitOfWork(db), Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 }));

        var result = await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        result.AlertsCreated.Should().Be(1);
        result.OutboxEventsQueued.Should().Be(1);

        var alerts = await db.Alerts.ToListAsync();
        alerts.Should().ContainSingle(a =>
            a.AnomalyType == AnomalyTypeEnum.Overheat
            && a.Severity == AlertSeverityEnum.Critical
            && a.Status == AlertStatusEnum.Open);

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Should().ContainSingle(m =>
            m.Type == nameof(BatteryAnomalyDetectedEvent)
            && m.ProcessedAtUtc == null);

        var evt = JsonSerializer.Deserialize<BatteryAnomalyDetectedEvent>(outbox[0].Payload);
        evt!.AssetSerialNumber.Should().Be("BAT-E2E-001");
        evt.AnomalyType.Should().Be((int)AnomalyTypeEnum.Overheat);
    }

    [Fact]
    /// <summary>
    /// Sprint 6.2 NOTI-08 (#679) — anomaly Warning nay publish <c>BatteryAnomalyWarningDetectedEvent</c>
    /// để NotificationService báo Customer (spec §3.4 T#12). Điều kiện bất biến quan trọng: KHÔNG được
    /// publish <c>BatteryAnomalyDetectedEvent</c> ở mức Warning — đó là event auto-tạo ticket.
    /// </summary>
    public async Task EndToEnd_WarningAnomaly_PublishesWarningNotifyEventOnly()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.SensorReadings.Add(new SensorReading
        {
            Time = DateTime.UtcNow.AddSeconds(-10),
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 53,
            SocPercent = 60
        });
        await db.SaveChangesAsync();

        var svc = new AnomalyDetectionService(
            new UnitOfWork(db), Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 }));

        await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        (await db.Alerts.SingleAsync()).Severity.Should().Be(AlertSeverityEnum.Warning);

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].Type.Should().Be(nameof(SharedContracts.Events.BatteryAnomalyWarningDetectedEvent));
        outbox.Should().NotContain(m => m.Type == nameof(SharedContracts.Events.BatteryAnomalyDetectedEvent));
    }

    /// <summary>Tắt cờ config → giữ nguyên hành vi cũ: Warning chỉ ghi alert, không publish gì.</summary>
    [Fact]
    public async Task EndToEnd_WarningAnomaly_WhenNotifyDisabled_NoOutbox()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.SensorReadings.Add(new SensorReading
        {
            Time = DateTime.UtcNow.AddSeconds(-10),
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 53,
            SocPercent = 60
        });
        await db.SaveChangesAsync();

        var svc = new AnomalyDetectionService(
            new UnitOfWork(db),
            Options.Create(new AnomalyEngineOptions
            {
                DedupWindowMinutes = 30,
                PublishWarningNotifications = false
            }));

        await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        (await db.Alerts.SingleAsync()).Severity.Should().Be(AlertSeverityEnum.Warning);
        (await db.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EndToEnd_DedupWithinWindow_MergesAlert()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var now = DateTime.UtcNow;
        db.SensorReadings.Add(new SensorReading
        {
            Time = now.AddMinutes(-2),
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 65,
            SocPercent = 60
        });
        await db.SaveChangesAsync();
        var svc = new AnomalyDetectionService(
            new UnitOfWork(db), Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 }));
        await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(3), CancellationToken.None);

        // Reading 2 — phải merge vào alert gốc. Lookback hẹp để chỉ scan reading mới này.
        db.SensorReadings.Add(new SensorReading
        {
            Time = now,
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 70,
            SocPercent = 60
        });
        await db.SaveChangesAsync();
        await svc.ScanRecentReadingsAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        var alerts = await db.Alerts.OrderBy(a => a.DetectedAt).ToListAsync();
        alerts.Should().HaveCount(2);
        var openAlerts = alerts.Where(a => a.Status == AlertStatusEnum.Open).ToList();
        var mergedAlerts = alerts.Where(a => a.Status == AlertStatusEnum.Merged).ToList();
        openAlerts.Should().HaveCount(1);
        mergedAlerts.Should().HaveCount(1);
        mergedAlerts[0].MergedIntoAlertId.Should().Be(openAlerts[0].Id);
        // Sprint IoT-2 #IoT2-30 — emit cả V1 + V2 event cho alert gốc (Saga subscribe cả 2 trong cutover).
        (await db.OutboxMessages.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task EndToEnd_SohDegradation_CriticalAlert()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.SensorReadings.Add(new SensorReading
        {
            Time = DateTime.UtcNow.AddSeconds(-10),
            BatteryAssetId = AssetId,
            Voltage = 12,
            Current = 0,
            Temperature = 25,
            SocPercent = 60,
            SohPercent = 70 // < SohCritical 75 → Critical
        });
        await db.SaveChangesAsync();

        var svc = new AnomalyDetectionService(
            new UnitOfWork(db), Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 }));
        await svc.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        (await db.Alerts.SingleAsync()).AnomalyType.Should().Be(AnomalyTypeEnum.SohDegradation);
        // Sprint IoT-2 #IoT2-30 — dual emit V1 + V2 event.
        (await db.OutboxMessages.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task EndToEnd_RelayOutbox_PublishesAndMarksProcessed()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var evt = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: AssetId,
            CustomerId: CustomerId,
            AssetSerialNumber: "BAT-E2E-001",
            AnomalyType: 1, Severity: 3,
            ThresholdValue: 50, ActualValue: 60, Unit: "°C",
            DetectedAt: DateTime.UtcNow);
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            Type = nameof(BatteryAnomalyDetectedEvent),
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow.AddSeconds(-2)
        });
        await db.SaveChangesAsync();

        var publishedEvents = new List<IntegrationEvent>();
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<IntegrationEvent, CancellationToken>((e, _) => publishedEvents.Add(e))
                .Returns(Task.CompletedTask);

        var svc = new OutboxRelayService(new UnitOfWork(db), producer.Object);
        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Published.Should().Be(1);
        publishedEvents.Should().HaveCount(1);
        publishedEvents[0].Should().BeOfType<BatteryAnomalyDetectedEvent>();

        var processedMsg = await db.OutboxMessages.SingleAsync();
        processedMsg.ProcessedAtUtc.Should().NotBeNull();
    }

    // ---------- Helpers ----------

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"battery-sprint3-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new CurrentUserService(new HttpContextAccessor());
        return new ApplicationDbContext(options, new AuditableEntityInterceptor(currentUser));
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.CustomerAccounts.Add(new CustomerAccount
        {
            Id = CustomerId,
            Email = "e2e@x.com",
            FullName = "E2E Customer",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = DateTime.UtcNow
        });
        db.BatteryTypes.Add(new BatteryType
        {
            Id = BatteryTypeId,
            Name = "LiFePO4 Test",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000
        });
        db.BatteryAssets.Add(new BatteryAsset
        {
            Id = AssetId,
            SerialNumber = "BAT-E2E-001",
            BatteryTypeId = BatteryTypeId,
            CustomerId = CustomerId,
            InstallDate = DateTime.UtcNow.AddDays(-30),
            Status = BatteryStatusEnum.Active,
            WarrantyStatus = WarrantyStatusEnum.Active
        });
        db.ThresholdConfigs.Add(new ThresholdConfig
        {
            Id = Guid.NewGuid(),
            BatteryTypeId = BatteryTypeId,
            VoltageMin = 10,
            VoltageMax = 14,
            TemperatureMin = -10,
            TemperatureMax = 50,
            SocWarningThreshold = 20,
            SocCriticalThreshold = 10,
            SohWarningThreshold = 85,
            SohCriticalThreshold = 75,
            NoiseSuppressionEnabled = false,    // Sprint 5B reconcile — test focuses on detection, not noise suppression
            EffectiveFromUtc = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
}
