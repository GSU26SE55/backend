using System.Reflection;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SharedContracts.Events;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>BE-AI — job nền: no-op khi disabled + convert readings đúng (cạm bẫy time/decimal).</summary>
public class SohPredictionBackgroundServiceTests
{
    private static SohPredictionBackgroundService Make(AiOptions options, IServiceScopeFactory scopeFactory)
        => new(scopeFactory, Options.Create(options), NullLogger<SohPredictionBackgroundService>.Instance);

    [Fact]
    public async Task Disabled_DoesNotCreateScope_NoOp()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var sut = Make(new AiOptions { Enabled = false }, scopeFactory.Object);

        // ExecuteAsync là protected — gọi qua StartAsync (BackgroundService) rồi stop ngay.
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await sut.StopAsync(cts.Token);

        // Enabled=false → return trước khi đụng scope factory. Strict mock => fail nếu bị gọi.
        scopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [Fact]
    public void BuildReadings_ConvertsTimeToRelativeSeconds_AndDecimalToDouble()
    {
        // Cạm bẫy #1: time PHẢI là giây tương đối từ reading đầu (KHÔNG phải DateTime tuyệt đối).
        var t0 = new DateTime(2026, 7, 22, 10, 0, 0, DateTimeKind.Utc);
        var window = new List<SensorReading>
        {
            MakeReading(t0, 3.9m, -1.0m, 25.0m),
            MakeReading(t0.AddSeconds(13), 3.88m, -1.1m, 25.5m),
            MakeReading(t0.AddSeconds(26), 3.86m, -1.2m, 26.0m),
        };

        var rows = InvokeBuildReadings(window);

        rows.Should().HaveCount(3);
        // Row 0: time = 0 (đầu window)
        rows[0][3].Should().Be(0.0);
        // Row 1: time = 13s tương đối
        rows[1][3].Should().Be(13.0);
        rows[2][3].Should().Be(26.0);
        // decimal → double, đúng thứ tự [voltage, current, temperature, time]
        rows[0][0].Should().BeApproximately(3.9, 1e-9);
        rows[0][1].Should().BeApproximately(-1.0, 1e-9);
        rows[0][2].Should().BeApproximately(25.0, 1e-9);
    }

    // ── GH-783 ───────────────────────────────────────────────────────────────────
    // Dedup cũ đòi `DedupWindowEndUtc > now` mà window chỉ dài 1 giờ → hết window là
    // tạo alert mới dù alert cũ vẫn Open (188 alert Open trên 9 asset ở E2E). Kèm theo,
    // /prescribe (RAG+LLM) chạy TRƯỚC dedup nên vẫn tốn cost cho alert sắp bị bỏ.

    [Fact]
    public async Task Tick_AssetHasUnresolvedAlert_DoesNotCallPrescribe()
    {
        var assetId = Guid.NewGuid();
        var harness = Harness.ForFailedPrediction(assetId, ExpiredOpenSohAlert(assetId));

        await harness.RunTickAsync();

        // Alert cũ vẫn Open → không được tốn RAG/LLM, không được tạo alert thứ hai.
        harness.Prescription.Verify(
            c => c.PrescribeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Uow.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
    }

    [Fact]
    public async Task ThreeTicks_PastDedupWindow_CreatesOnlyOneAlert()
    {
        var harness = Harness.ForFailedPrediction(Guid.NewGuid());

        await harness.RunTickAsync();
        harness.ExpireDedupWindows();   // giả lập > 1 giờ trôi qua, alert vẫn Open
        await harness.RunTickAsync();
        harness.ExpireDedupWindows();
        await harness.RunTickAsync();

        harness.Uow.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Once);
        harness.Alerts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ThreeTicks_PastDedupWindow_EmitsTicketEventOnlyOnce()
    {
        var harness = Harness.ForFailedPrediction(Guid.NewGuid());

        await harness.RunTickAsync();
        harness.ExpireDedupWindows();
        await harness.RunTickAsync();
        harness.ExpireDedupWindows();
        await harness.RunTickAsync();

        // 1 alert → đúng 1 cặp V1+V2 → saga tạo đúng 1 ticket (không nhân SLA).
        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);
    }

    [Fact]
    public async Task Tick_MergeWithoutEscalation_KeepsDetectedAt_SoEscalationClockStillRuns()
    {
        // AlertEscalationService lọc `DetectedAt <= now - EscalationAfterMinutes` (5 phút) còn job
        // này chạy mỗi 5 phút. Nếu merge đẩy DetectedAt = now thì alert SOH Critical không bao giờ
        // đủ già để escalate — luồng P1 chết âm thầm.
        var assetId = Guid.NewGuid();
        var existing = ExpiredOpenSohAlert(assetId);   // đã Critical → merge thuần, không escalate
        var detectedAtBefore = existing.DetectedAt;
        var harness = Harness.ForFailedPrediction(assetId, existing);

        await harness.RunTickAsync();

        existing.DetectedAt.Should().Be(detectedAtBefore);
        existing.ActualValue.Should().Be(72.5m);                        // evidence vẫn được refresh
        existing.DedupWindowEndUtc.Should().BeAfter(DateTime.UtcNow);   // window vẫn được gia hạn
    }

    [Fact]
    public async Task Tick_OpenWarningAlert_PredictionFailed_EscalatesAndEmitsTicketEvent()
    {
        // Alert Warning đang mở chiếm chỗ dedup → nếu chỉ merge thuần, pin chuyển sang Failed
        // sẽ không bao giờ có ticket. Phải nâng severity + bắn event đúng một lần.
        var assetId = Guid.NewGuid();
        var warning = ExpiredOpenSohAlert(assetId);
        warning.Severity = AlertSeverityEnum.Warning;
        var harness = Harness.ForFailedPrediction(assetId, warning);

        await harness.RunTickAsync();

        warning.Severity.Should().Be(AlertSeverityEnum.Critical);
        harness.Uow.Alerts.Verify(r => r.AddAsync(It.IsAny<Alert>()), Times.Never);
        harness.Prescription.Verify(
            c => c.PrescribeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);
    }

    [Fact]
    public async Task Tick_AfterEscalation_DoesNotEmitTicketEventAgain()
    {
        var assetId = Guid.NewGuid();
        var warning = ExpiredOpenSohAlert(assetId);
        warning.Severity = AlertSeverityEnum.Warning;
        var harness = Harness.ForFailedPrediction(assetId, warning);

        await harness.RunTickAsync();   // escalate
        harness.ExpireDedupWindows();
        await harness.RunTickAsync();   // đã Critical → chỉ refresh, không lặp lại event

        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedEvent)).Should().Be(1);
        harness.Outbox.Count(m => m.Type == nameof(BatteryAnomalyDetectedV2Event)).Should().Be(1);
    }

    private static Alert ExpiredOpenSohAlert(Guid assetId) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = assetId,
        AnomalyType = AnomalyTypeEnum.SohDegradation,
        Severity = AlertSeverityEnum.Critical,
        Status = AlertStatusEnum.Open,
        DetectedAt = DateTime.UtcNow.AddHours(-2),
        DedupWindowEndUtc = DateTime.UtcNow.AddHours(-1),   // window đã hết, alert chưa resolve
    };

    /// <summary>
    /// Dựng 1 tick chạy được: 1 asset Active đủ reading, AI luôn trả Failed.
    /// <c>RunTickAsync</c> là private nên gọi qua reflection (cùng lối với BuildReadings ở trên).
    /// </summary>
    private sealed class Harness
    {
        private static readonly MethodInfo RunTick = typeof(SohPredictionBackgroundService)
            .GetMethod("RunTickAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private SohPredictionBackgroundService _sut = null!;

        public MockUnitOfWorkBuilder Uow { get; private init; } = null!;
        public Mock<IAiPrescriptionClient> Prescription { get; private init; } = null!;

        public List<Alert> Alerts => Uow.Alerts.Object.GetAllAsync().ToList();
        public List<OutboxMessage> Outbox => Uow.OutboxMessages.Object.GetAllAsync().ToList();

        public static Harness ForFailedPrediction(Guid assetId, params Alert[] existingAlerts)
        {
            var t0 = DateTime.UtcNow.AddMinutes(-5);
            var asset = new BatteryAsset
            {
                Id = assetId,
                SerialNumber = "SN-GH783",
                CustomerId = Guid.NewGuid(),
                SiteId = Guid.NewGuid(),
                Status = BatteryStatusEnum.Active,
                BatteryType = new BatteryType
                {
                    NominalVoltage = 12.8m,
                    NominalCapacityAh = 100m,
                    Chemistry = BatteryChemistryEnum.LiFePO4,
                },
            };

            var uow = new MockUnitOfWorkBuilder()
                .WithBatteryAssets(asset)
                .WithSensorReadings(
                    AssetReading(assetId, t0),
                    AssetReading(assetId, t0.AddSeconds(13)),
                    AssetReading(assetId, t0.AddSeconds(26)))
                .WithAlerts(existingAlerts);

            var prediction = new Mock<IAiPredictionClient>();
            prediction
                .Setup(c => c.PredictAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                    It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FailedPrediction());

            var prescription = new Mock<IAiPrescriptionClient>();
            prescription
                .Setup(c => c.PrescribeAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                    It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AiPrescriptionResult?)null);

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IBatteryUnitOfWork))).Returns(uow.Build());
            provider.Setup(p => p.GetService(typeof(IAiPredictionClient))).Returns(prediction.Object);
            provider.Setup(p => p.GetService(typeof(IAiPrescriptionClient))).Returns(prescription.Object);

            var scope = new Mock<IServiceScope>();
            scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);

            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

            var harness = new Harness { Uow = uow, Prescription = prescription };
            harness._sut = Make(
                new AiOptions { Enabled = true, MinReadings = 3, PrescriptionEnabled = true },
                scopeFactory.Object);
            return harness;
        }

        public Task RunTickAsync()
            => (Task)RunTick.Invoke(_sut, new object[] { CancellationToken.None })!;

        /// <summary>Giả lập thời gian trôi qua cửa sổ dedup — alert vẫn Open, window đã hết hạn.</summary>
        public void ExpireDedupWindows()
        {
            foreach (var alert in Alerts)
                alert.DedupWindowEndUtc = DateTime.UtcNow.AddHours(-1);
        }

        private static SensorReading AssetReading(Guid assetId, DateTime time) => new()
        {
            Time = time,
            BatteryAssetId = assetId,
            Voltage = 12.4m,
            Current = -1.2m,
            Temperature = 31.0m,
            SocPercent = 40m,
            SourceType = SensorReadingSourceTypeEnum.Bms,
        };

        private static AiPredictionResult FailedPrediction() => new(
            SohPercent: 72.5m,
            Confidence: 0.91m,
            Classification: AnomalyClassificationEnum.Failed,
            AnomalyScore: -0.42m,
            AnomalyConfidence: 0.42m,
            RulCyclesEstimate: 40,
            Priority: "P1",
            ModelVersion: "1.6",
            LatencyMs: 87);
    }

    private static SensorReading MakeReading(DateTime time, decimal v, decimal i, decimal temp) => new()
    {
        Time = time,
        BatteryAssetId = Guid.NewGuid(),
        Voltage = v,
        Current = i,
        Temperature = temp,
        SocPercent = 50m,
        SourceType = SensorReadingSourceTypeEnum.Bms,
    };

    private static IReadOnlyList<double[]> InvokeBuildReadings(IReadOnlyList<SensorReading> window)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("BuildReadings", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IReadOnlyList<double[]>)method.Invoke(null, new object[] { window })!;
    }
}
