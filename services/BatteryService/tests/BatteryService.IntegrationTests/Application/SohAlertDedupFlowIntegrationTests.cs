using System.Reflection;
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
/// GH-783 — chạy nhiều tick SohPredictionBackgroundService qua cửa sổ dedup trên
/// <see cref="ApplicationDbContext"/> + <see cref="UnitOfWork"/> THẬT (không mock repo).
///
/// Khác unit test ở chỗ: EF change tracking, <c>AuditableEntityInterceptor</c> và
/// <c>SaveChangesAsync</c> đều chạy thật — nên nếu merge in-place không thực sự persist
/// (vd quên <c>UpdateAsync</c>, hoặc entity bị detach) thì test này bắt được, unit test thì không.
/// </summary>
public class SohAlertDedupFlowIntegrationTests
{
    private static readonly Guid BatteryTypeId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task ThreeTicksAcrossDedupWindow_KeepsSingleAlert_AndSingleTicketEventPair()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        var sut = CreateService(db, AnomalyClassificationEnum.Failed);

        await RunTickAsync(sut);
        await ExpireDedupWindowsAsync(db);   // giả lập > 1 giờ trôi qua, alert vẫn Open
        await RunTickAsync(sut);
        await ExpireDedupWindowsAsync(db);
        await RunTickAsync(sut);

        var alerts = await db.Alerts.Where(a => a.AnomalyType == AnomalyTypeEnum.SohDegradation).ToListAsync();
        alerts.Should().HaveCount(1, "dedup theo Status phải merge in-place, không sinh alert mỗi giờ");
        alerts[0].Status.Should().Be(AlertStatusEnum.Open);
        alerts[0].DedupWindowEndUtc.Should().BeAfter(DateTime.UtcNow, "window phải được gia hạn ở tick cuối");

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);

        // 3 tick vẫn phải lưu đủ 3 prediction — dedup chỉ chặn Alert, không chặn lịch sử chart.
        (await db.SohPredictions.CountAsync()).Should().Be(3);
        (await db.AnomalyClassifications.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task MergeTick_PersistsRefreshedEvidence_ButKeepsDetectedAt()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Failed, sohPercent: 70m));
        var afterFirst = await db.Alerts.SingleAsync(a => a.AnomalyType == AnomalyTypeEnum.SohDegradation);
        var detectedAtBefore = afterFirst.DetectedAt;

        await ExpireDedupWindowsAsync(db);
        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Failed, sohPercent: 61m));

        var merged = await db.Alerts.SingleAsync(a => a.AnomalyType == AnomalyTypeEnum.SohDegradation);
        merged.ActualValue.Should().Be(61m, "evidence phải được refresh và thực sự persist");
        merged.DetectedAt.Should().Be(detectedAtBefore,
            "DetectedAt phải giữ nguyên, nếu không AlertEscalationService sẽ không bao giờ escalate alert SOH");
    }

    [Fact]
    public async Task DegradingThenFailed_EscalatesInPlace_AndEmitsExactlyOneTicketEventPair()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Degrading));
        var warning = await db.Alerts.SingleAsync(a => a.AnomalyType == AnomalyTypeEnum.SohDegradation);
        warning.Severity.Should().Be(AlertSeverityEnum.Warning);
        (await db.OutboxMessages.CountAsync()).Should().Be(0, "Warning không sinh ticket");

        await ExpireDedupWindowsAsync(db);
        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Failed));

        var escalated = await db.Alerts.SingleAsync(a => a.AnomalyType == AnomalyTypeEnum.SohDegradation);
        escalated.Id.Should().Be(warning.Id, "phải nâng cấp tại chỗ, không tạo alert thứ hai");
        escalated.Severity.Should().Be(AlertSeverityEnum.Critical);

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);
        outbox.Should().AllSatisfy(m => m.AggregateId.Should().Be(warning.Id));

        // Tick tiếp theo khi đã Critical → chỉ refresh, không bắn event lần hai.
        await ExpireDedupWindowsAsync(db);
        await RunTickAsync(CreateService(db, AnomalyClassificationEnum.Failed));
        (await db.OutboxMessages.CountAsync()).Should().Be(2);
    }

    // ---------- Helpers ----------

    private static async Task RunTickAsync(SohPredictionBackgroundService sut)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("RunTickAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    /// <summary>Đẩy mọi cửa sổ dedup về quá khứ — alert vẫn Open, chỉ window hết hạn.</summary>
    private static async Task ExpireDedupWindowsAsync(ApplicationDbContext db)
    {
        foreach (var alert in await db.Alerts.ToListAsync())
            alert.DedupWindowEndUtc = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// GH-805 — <paramref name="priority"/> mặc định "None" để các test dedup ở đây chỉ do
    /// classification điều khiển. Trước GH-805 fixture hardcode "P1" (vô hại vì priority bị bỏ qua);
    /// nay severity gộp cả risk.priority nên "P1" sẽ nâng Degrading thành Critical và làm hỏng
    /// kịch bản escalation Warning → Critical bên dưới.
    /// </summary>
    private static SohPredictionBackgroundService CreateService(
        ApplicationDbContext db, AnomalyClassificationEnum classification, decimal sohPercent = 72.5m,
        string priority = "None")
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
                AnomalyScore: -0.4m,
                AnomalyConfidence: 0.4m,
                RulCyclesEstimate: 40,
                Priority: priority,
                ModelVersion: "1.6",
                LatencyMs: 87));

        var prescription = new Mock<IAiPrescriptionClient>();
        prescription
            .Setup(c => c.PrescribeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiPrescriptionResult(
                Prescription: "Thay pin",
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
            .UseInMemoryDatabase($"battery-gh783-{Guid.NewGuid()}")
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
            Name = "LiFePO4 GH783",
            NominalCapacityAh = 100,
            NominalVoltage = 12.8m,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000,
        });
        db.BatteryAssets.Add(new BatteryAsset
        {
            Id = AssetId,
            SerialNumber = "GH783-INT",
            BatteryTypeId = BatteryTypeId,
            CustomerId = CustomerId,
            InstallDate = DateTime.UtcNow.AddYears(-2),
            Status = BatteryStatusEnum.Active,
        });

        var t0 = DateTime.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 3; i++)
        {
            db.SensorReadings.Add(new SensorReading
            {
                Time = t0.AddSeconds(i * 13),
                BatteryAssetId = AssetId,
                Voltage = 12.4m,
                Current = -1.2m,
                Temperature = 31m,
                SocPercent = 40m,
                SourceType = SensorReadingSourceTypeEnum.Bms,
            });
        }

        await db.SaveChangesAsync();
    }
}
