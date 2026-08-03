using System.Reflection;
using System.Text.Json;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// GH-805 — alert phải nổ theo <c>risk.priority</c> P1/P2 kể cả khi classification vẫn Normal.
///
/// Repro trong issue: SOH 95 · isolation score 0 · nhiệt 50°C → AI trả classification=Normal,
/// health=Healthy, warnings=[TEMP_CRITICAL], risk_level=Critical, priority=P1. Trước fix BE bỏ qua
/// hoàn toàn → sự cố nhiệt không có alert lẫn ticket.
///
/// Chạy trên <see cref="ApplicationDbContext"/> + <see cref="UnitOfWork"/> THẬT để bắt được cả lỗi
/// persist (AnomalyType/AiEvidence có thực sự xuống DB không), thứ unit test mock repo bỏ lọt.
/// </summary>
public class SohRiskAlertIntegrationTests
{
    private static readonly Guid BatteryTypeId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task NormalWithP1AndCriticalTempWarning_RaisesOverheatCriticalAlertAndTicketEvents()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        // Đúng payload repro của issue.
        var sut = CreateService(
            db,
            AnomalyClassificationEnum.Normal,
            priority: "P1",
            sohPercent: 95m,
            anomalyScore: 0m,
            riskLevel: "Critical",
            actionCode: "INSPECT_THERMAL",
            warnings: new[] { new AiWarningItem("TEMP_CRITICAL", "critical", "Temperature 50C exceeds limit") });

        await RunTickAsync(sut);

        var alert = await db.Alerts.SingleAsync();
        alert.Severity.Should().Be(AlertSeverityEnum.Critical, "priority P1 phải map sang Critical");
        alert.AnomalyType.Should().Be(AnomalyTypeEnum.Overheat,
            "TEMP_CRITICAL là sự cố nhiệt — gán SohDegradation sẽ ra ticket P3 / SLA 72h");
        alert.Status.Should().Be(AlertStatusEnum.Open);

        // Ngưỡng/giá trị SOH không có nghĩa với alert nhiệt.
        alert.ThresholdValue.Should().BeNull();
        alert.ActualValue.Should().BeNull();
        alert.Unit.Should().BeNull();

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);

        // Event phải mang type thật, nếu không TicketService vẫn phân loại nhầm về Performance/Low.
        using var payload = JsonDocument.Parse(
            outbox.Single(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Payload);
        payload.RootElement.GetProperty("AnomalyType").GetInt32()
            .Should().Be((int)AnomalyTypeEnum.Overheat);
    }

    [Fact]
    public async Task NormalWithP1_PersistsAiEvidence()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var sut = CreateService(
            db,
            AnomalyClassificationEnum.Normal,
            priority: "P1",
            riskLevel: "Critical",
            actionCode: "INSPECT_THERMAL",
            warnings: new[] { new AiWarningItem("TEMP_CRITICAL", "critical", "Temperature 50C exceeds limit") });

        await RunTickAsync(sut);

        var alert = await db.Alerts.SingleAsync();
        alert.AiEvidence.Should().NotBeNullOrWhiteSpace(
            "alert Critical với SOH 95% là vô lý nếu không ghi lại lý do");

        using var evidence = JsonDocument.Parse(alert.AiEvidence!);
        var root = evidence.RootElement;
        root.GetProperty("risk_level").GetString().Should().Be("Critical");
        root.GetProperty("priority").GetString().Should().Be("P1");
        root.GetProperty("action_code").GetString().Should().Be("INSPECT_THERMAL");

        var warnings = root.GetProperty("warnings");
        warnings.GetArrayLength().Should().Be(1);
        warnings[0].GetProperty("code").GetString().Should().Be("TEMP_CRITICAL");
        warnings[0].GetProperty("severity").GetString().Should().Be("critical");
        warnings[0].GetProperty("message").GetString().Should().Be("Temperature 50C exceeds limit");
    }

    [Fact]
    public async Task RefreshTick_WithoutAiEvidence_DoesNotWipeStoredEvidence()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        // Tick 1 — alert SOH kèm bằng chứng đầy đủ.
        await RunTickAsync(CreateService(
            db, AnomalyClassificationEnum.Failed, sohPercent: 61m,
            riskLevel: "Critical", actionCode: "REPLACE",
            warnings: new[] { new AiWarningItem("SOH_LOW", "critical", "SOH below EOL") }));

        var afterFirst = await db.Alerts.SingleAsync();
        afterFirst.AiEvidence.Should().NotBeNull();

        // Tick 2 — cùng asset, cùng type, nhưng AI không trả risk lẫn warning lần này.
        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Failed, sohPercent: 60m));

        var refreshed = await db.Alerts.SingleAsync();
        refreshed.ActualValue.Should().Be(60m, "evidence số vẫn phải được refresh");
        refreshed.AiEvidence.Should().NotBeNull(
            "tick không có warning không được xoá lý do alert đã nổ — AC 'preserve warning/risk details'");
    }

    [Fact]
    public async Task NormalWithP2_RaisesWarningAlert_WithoutTicketEvents()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var sut = CreateService(
            db,
            AnomalyClassificationEnum.Normal,
            priority: "P2",
            riskLevel: "High",
            warnings: new[] { new AiWarningItem("VOLTAGE_LOW", "warning", "Cell voltage sagging") });

        await RunTickAsync(sut);

        var alert = await db.Alerts.SingleAsync();
        alert.Severity.Should().Be(AlertSeverityEnum.Warning, "priority P2 map sang Warning");
        alert.AnomalyType.Should().Be(AnomalyTypeEnum.Undervoltage);

        (await db.OutboxMessages.CountAsync()).Should()
            .Be(0, "chỉ Critical mới sinh ticket — giữ nguyên convention threshold engine");
    }

    [Theory]
    [InlineData("P3")]
    [InlineData("None")]
    [InlineData("")]
    public async Task NormalWithoutActionablePriority_RaisesNothing(string priority)
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var sut = CreateService(db, AnomalyClassificationEnum.Normal, priority: priority);

        await RunTickAsync(sut);

        (await db.Alerts.CountAsync()).Should().Be(0, "hành vi trước GH-805 phải được giữ nguyên");
        (await db.OutboxMessages.CountAsync()).Should().Be(0);

        // Prediction vẫn được lưu — không alert không có nghĩa là không chạy model.
        (await db.SohPredictions.CountAsync()).Should().Be(1);
        (await db.AnomalyClassifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FailedWithP1_RaisesExactlyOneAlertAndOneEventPair()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        // Hai tín hiệu cùng bật — AC "không duplicate với classification branch".
        var sut = CreateService(
            db,
            AnomalyClassificationEnum.Failed,
            priority: "P1",
            sohPercent: 61m,
            riskLevel: "Critical",
            warnings: new[] { new AiWarningItem("SOH_LOW", "critical", "SOH below EOL") });

        await RunTickAsync(sut);

        var alerts = await db.Alerts.ToListAsync();
        alerts.Should().HaveCount(1, "hai nguồn tín hiệu chỉ được sinh một alert");
        alerts[0].Severity.Should().Be(AlertSeverityEnum.Critical);
        alerts[0].AnomalyType.Should().Be(AnomalyTypeEnum.SohDegradation);
        alerts[0].ActualValue.Should().Be(61m, "alert SOH vẫn giữ nguyên threshold/actual như trước");
        alerts[0].ThresholdValue.Should().Be(80m);
        alerts[0].Unit.Should().Be("%");

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);
    }

    [Fact]
    public async Task FailedWithP2_StaysCritical()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var sut = CreateService(db, AnomalyClassificationEnum.Failed, priority: "P2");

        await RunTickAsync(sut);

        var alert = await db.Alerts.SingleAsync();
        alert.Severity.Should().Be(AlertSeverityEnum.Critical,
            "pin đã hỏng không được P2 hạ xuống Warning — sẽ mất ticket");
    }

    [Fact]
    public async Task DegradingWithP1_IsRaisedToCritical()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var sut = CreateService(db, AnomalyClassificationEnum.Degrading, priority: "P1");

        await RunTickAsync(sut);

        var alert = await db.Alerts.SingleAsync();
        alert.Severity.Should().Be(AlertSeverityEnum.Critical, "lấy mức cao hơn giữa hai nguồn");
    }

    [Fact]
    public async Task OpenSohAlert_DoesNotSwallowNewOverheatAlert()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        // Tick 1 — alert SohDegradation.
        await RunTickAsync(CreateService(
            db, AnomalyClassificationEnum.Failed, priority: "None", sohPercent: 61m,
            warnings: new[] { new AiWarningItem("SOH_LOW", "critical", "SOH below EOL") }));

        // Tick 2 — sự cố nhiệt trên cùng asset, alert SOH vẫn đang Open.
        await RunTickAsync(CreateService(
            db, AnomalyClassificationEnum.Normal, priority: "P1", sohPercent: 61m,
            warnings: new[] { new AiWarningItem("TEMP_CRITICAL", "critical", "Temperature 50C exceeds limit") }));

        var alerts = await db.Alerts.ToListAsync();
        alerts.Should().HaveCount(2,
            "dedup phải theo AnomalyType — nếu lọc cứng SohDegradation thì alert nhiệt bị nuốt mất");
        alerts.Select(a => a.AnomalyType).Should()
            .BeEquivalentTo(new[] { AnomalyTypeEnum.SohDegradation, AnomalyTypeEnum.Overheat });
    }

    // ---------- Helpers ----------

    private static async Task RunTickAsync(SohPredictionBackgroundService sut)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("RunTickAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    private static SohPredictionBackgroundService CreateService(
        ApplicationDbContext db,
        AnomalyClassificationEnum classification,
        string priority = "None",
        decimal sohPercent = 95m,
        decimal anomalyScore = 0m,
        string? riskLevel = null,
        string? actionCode = null,
        IReadOnlyList<AiWarningItem>? warnings = null)
    {
        var prediction = new Mock<IAiPredictionClient>();
        prediction
            .Setup(c => c.PredictAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiPredictionResult(
                SohPercent: sohPercent,
                Confidence: 0.9m,
                Classification: classification,
                AnomalyScore: anomalyScore,
                AnomalyConfidence: 0.1m,
                RulCyclesEstimate: 400,
                Priority: priority,
                ModelVersion: "1.6",
                LatencyMs: 87,
                RiskLevel: riskLevel,
                ActionCode: actionCode,
                Warnings: warnings));

        var prescription = new Mock<IAiPrescriptionClient>();
        prescription
            .Setup(c => c.PrescribeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiPrescriptionResult(
                Prescription: "Kiem tra tan nhiet",
                ActionSteps: new[] { "Ngat tai" },
                PpeRequired: new[] { "Gang tay cach dien" },
                SopReferences: Array.Empty<string>(),
                SafetyWarnings: Array.Empty<string>(),
                HumanVerificationRequired: true,
                Enriched: true,
                LlmProvider: "deepseek"));

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IBatteryUnitOfWork))).Returns(new UnitOfWork(db));
        provider.Setup(p => p.GetService(typeof(IAiPredictionClient))).Returns(prediction.Object);
        provider.Setup(p => p.GetService(typeof(IAiPrescriptionClient))).Returns(prescription.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new SohPredictionBackgroundService(
            scopeFactory.Object,
            Options.Create(new AiOptions { Enabled = true, MinReadings = 3, PrescriptionEnabled = true }),
            NullLogger<SohPredictionBackgroundService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"battery-gh805-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new CurrentUserService(new HttpContextAccessor());
        return new ApplicationDbContext(options, new AuditableEntityInterceptor(currentUser));
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.BatteryTypes.Add(new BatteryType
        {
            Id = BatteryTypeId,
            Name = "LiFePO4 GH805",
            NominalCapacityAh = 100,
            NominalVoltage = 12.8m,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000,
        });
        db.BatteryAssets.Add(new BatteryAsset
        {
            Id = AssetId,
            SerialNumber = "GH805-INT",
            BatteryTypeId = BatteryTypeId,
            CustomerId = CustomerId,
            InstallDate = DateTime.UtcNow.AddYears(-1),
            Status = BatteryStatusEnum.Active,
        });

        var t0 = DateTime.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 3; i++)
        {
            db.SensorReadings.Add(new SensorReading
            {
                Time = t0.AddSeconds(i * 13),
                BatteryAssetId = AssetId,
                Voltage = 12.9m,
                Current = -1.2m,
                Temperature = 50m,
                SocPercent = 80m,
                SourceType = SensorReadingSourceTypeEnum.Bms,
            });
        }

        await db.SaveChangesAsync();
    }
}
