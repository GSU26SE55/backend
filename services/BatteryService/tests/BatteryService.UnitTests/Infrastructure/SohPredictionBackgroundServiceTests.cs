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
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()),
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
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()),
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

    // ── GH-762 ───────────────────────────────────────────────────────────────────
    // Job gom đúng MinReadings mẫu gần nhất rồi gửi thẳng. AI duyệt TỪNG dòng và ném lỗi ở dòng
    // lệch dải ĐẦU TIÊN ⇒ một số đo bất khả thi làm hỏng cả cửa sổ, job nhận null rồi `continue`,
    // pin đó không có prediction nào cho tới khi số đo ấy tự rơi khỏi cửa sổ.
    // Bằng chứng runtime: BAT-2026-001 (LiFePO4 12 V) có một mẫu 52.40 V ⇒ 13.10 V/cell.

    [Fact]
    public async Task Tick_SingleOutlierInWindow_StillPredicts_UsingOlderCleanReading()
    {
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        // 30 mẫu sạch + 1 mẫu 52.4 V là MỚI NHẤT — trường hợp xấu nhất, vì cửa sổ luôn gồm mẫu
        // mới nhất nên chắc chắn dính.
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize + 1,
                (i, r) => { if (i == AiOptions.WindowSize) r.Voltage = 52.4m; }));

        await harness.RunTickAsync();

        // Trước bản sửa: AI bị gọi với cả mẫu 52.4 V và từ chối ⇒ 0 prediction.
        harness.Predictions.Should().HaveCount(1);
        harness.LastReadingsSentToAi.Should().NotBeNull();
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        harness.LastReadingsSentToAi!.Should().NotContain(row => row[0] > 50);
    }

    [Fact]
    public async Task Tick_OutlierRemoved_TimeColumnStillStartsAtZero()
    {
        // Cột `time` là GIÂY TƯƠNG ĐỐI so với mẫu đầu cửa sổ. Nếu lọc SAU khi dựng dòng thì gốc
        // thời gian rơi vào một mẫu đã bị loại, cột `time` không còn bắt đầu từ 0, và phân phối
        // đầu vào model lệch đi mà không có gì báo.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize + 1,
                (i, r) => { if (i == 0) r.Voltage = 52.4m; }));   // mẫu CŨ NHẤT hỏng ⇒ bị loại

        await harness.RunTickAsync();

        harness.LastReadingsSentToAi.Should().NotBeNull();
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        harness.LastReadingsSentToAi![0][3].Should().Be(0.0);
        harness.LastReadingsSentToAi![1][3].Should().Be(13.0);
        harness.LastReadingsSentToAi![2][3].Should().Be(26.0);
    }

    [Fact]
    public async Task Tick_PredictionWindowTimestamps_MatchTheReadingsActuallySent()
    {
        // Cửa sổ ghi vào SohPrediction phải là các mẫu THỰC SỰ gửi đi, không phải toàn dải đã
        // quét — nếu không, biểu đồ dashboard hiển thị một khoảng rộng hơn sự thật.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize + 1,
                (i, r) => { if (i == 0) r.Voltage = 52.4m; }));

        await harness.RunTickAsync();

        var prediction = harness.Predictions.Should().ContainSingle().Subject;
        prediction.InputWindowStartUtc.Should().Be(t0.AddSeconds(13));   // KHÔNG phải t0
        prediction.InputWindowEndUtc.Should().Be(t0.AddSeconds(13 * AiOptions.WindowSize));
    }

    [Fact]
    public async Task Tick_TooManyOutliers_SkipsAssetWithoutCallingAi()
    {
        // Không đủ mẫu sạch thì bỏ qua lượt — nhưng phải bỏ qua TRƯỚC khi gọi AI, chứ không phải
        // gọi rồi để AI từ chối (tốn một vòng mạng cho một payload chắc chắn hỏng).
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        // 31 mẫu nhưng 2 mẫu hỏng ⇒ chỉ còn 29 sạch, thiếu 1 so với cửa sổ bắt buộc.
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize + 1,
                (i, r) => { if (i is 5 or 9) r.Voltage = 52.4m; }));

        await harness.RunTickAsync();

        harness.Predictions.Should().BeEmpty();
        harness.Prediction.Verify(
            c => c.PredictAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_CleanWindow_BehavesExactlyAsBefore()
    {
        // Chống hồi quy: dữ liệu sạch phải đi đúng đường cũ — bản sửa không được đổi gì ở đây.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(assetId, Window(assetId, t0, AiOptions.WindowSize));

        await harness.RunTickAsync();

        harness.Predictions.Should().HaveCount(1);
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        harness.LastReadingsSentToAi![0][3].Should().Be(0.0);
    }

    // ── GH-777 ───────────────────────────────────────────────────────────────────────────────
    // Worker đọc SensorReading có sẵn CycleCount và SocPercent nhưng chỉ gửi 4 cột legacy
    // [V, I, T, time]. AI vì thế phải thay cycle bằng 0 và tự ước lượng SOC từ chính cửa sổ 30 mẫu
    // — ước lượng cục bộ, kém hẳn số đo thật lấy từ toàn bộ lịch sử sạc/xả. Đo được cùng seed cùng
    // model: 4 cột ra SOH 67.33, 6 cột (cycle=150, SOC=20) ra 40.46 — lệch 26.87 điểm.

    [Fact]
    public async Task Tick_AllReadingsHaveCycleCount_SendsSixColumns()
    {
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize, (i, r) =>
            {
                r.CycleCount = 150 + i;
                r.SocPercent = 20m + i;
            }));

        await harness.RunTickAsync();

        var rows = harness.LastReadingsSentToAi!;
        rows.Should().HaveCount(AiOptions.WindowSize);
        rows.Should().OnlyContain(r => r.Length == 6);
        // Thứ tự cột theo hợp đồng AI: [voltage, current, temperature, time, cycle_count, soc_percent].
        // Đảo hai cột cuối là gửi SOC vào chỗ cycle mà không có gì đỏ — model chỉ ra số sai.
        rows[0][4].Should().Be(150);
        rows[0][5].Should().Be(20);
        rows[^1][4].Should().Be(150 + AiOptions.WindowSize - 1);
        rows[^1][5].Should().Be(20 + AiOptions.WindowSize - 1);
    }

    [Fact]
    public async Task Tick_MissingCycleCount_OnWindowModeArtifact_FallsBackToFourColumns()
    {
        // Hợp đồng AI đòi cycle/soc "tất cả hoặc không". Gửi cửa sổ nửa nọ nửa kia sẽ bị từ chối
        // NGUYÊN KHỐI — đúng cái vòng câm lặng mà GH-762 vừa gỡ. CycleCount là nullable và thực tế
        // có mẫu thiếu, nên nhánh 4 cột là đường chạy thật chứ không phải "phòng hờ".
        //
        // Với bộ soc_mode="window", 4 cột LUÔN hợp lệ: AI tự tính SOC window-local đúng như
        // lúc train. Đây là nhánh giữ nguyên hành vi cũ.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadingsWithHealth(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize, (i, r) =>
            {
                r.CycleCount = i == 7 ? null : 150 + i;   // đúng MỘT mẫu thiếu cycle
                r.SocPercent = 50m;
            }),
            Harness.DefaultHealth(lfpSocMode: "window"));

        await harness.RunTickAsync();

        harness.LastReadingsSentToAi.Should().NotBeNull();
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        harness.LastReadingsSentToAi!.Should().OnlyContain(r => r.Length == 4);
    }

    [Fact]
    public async Task Tick_MissingCycleCount_OnCycleModeArtifact_SkipsWithoutCallingAi()
    {
        // Bộ soc_mode="cycle" TỪ CHỐI THẲNG payload 4 cột — quan sát được trong log thật:
        //   "LFP artifacts were trained with soc_mode='cycle' ... but this payload has 4 columns"
        // Nên hạ xuống 4 cột ở đây là cầm chắc bị từ chối, chỉ tốn một lượt gRPC cộng một lượt
        // HTTP fallback rồi vẫn về tay không. Phải dừng TRƯỚC khi gọi.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadingsWithHealth(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize, (i, r) =>
            {
                r.CycleCount = i == 7 ? null : 150 + i;
                r.SocPercent = 50m;
            }),
            Harness.DefaultHealth());   // lfpSocMode = "cycle"

        await harness.RunTickAsync();

        harness.LastReadingsSentToAi.Should().BeNull("không được gọi AI khi chắc chắn bị từ chối");
        harness.Predictions.Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_SixColumnWindow_StillStartsTimeAtZero()
    {
        // Chống hồi quy GH-762: thêm cột không được làm xê dịch gốc thời gian.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize, (i, r) =>
            {
                r.CycleCount = 10;
                r.SocPercent = 50m;
            }));

        await harness.RunTickAsync();

        harness.LastReadingsSentToAi![0][3].Should().Be(0.0);
        harness.LastReadingsSentToAi![1][3].Should().Be(13.0);
        harness.LastReadingsSentToAi![2][3].Should().Be(26.0);
    }

    [Fact]
    public async Task Tick_ReadingWithImpossibleSoc_IsQuarantined_NotSentToAi()
    {
        // Giao điểm GH-777 × GH-762: cửa sổ 6 cột có thêm soc_percent, và AI KIỂM cột đó. Không lọc
        // ở phía mình thì một SOC bất khả thi lại làm AI từ chối nguyên cửa sổ — đúng lỗi vừa gỡ.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var harness = Harness.ForReadings(
            assetId,
            Window(assetId, t0, AiOptions.WindowSize + 1, (i, r) =>
            {
                r.CycleCount = 10;
                r.SocPercent = i == AiOptions.WindowSize ? 250m : 50m;   // SOC 250% — bất khả thi
            }));

        await harness.RunTickAsync();

        harness.Predictions.Should().HaveCount(1);
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        harness.LastReadingsSentToAi!.Should().NotContain(r => r.Length >= 6 && r[5] > 100);
    }

    [Fact]
    public async Task Tick_CriticalAlert_StoresPrescriptionIdOnTheAlert()
    {
        // GH-778 — không lưu id thì nó chết ngay sau khi dựng xong đoạn text nhét vào ticket, và
        // vòng học của AI không bao giờ khép lại. Test này đi qua ĐÚNG đường client → alert, nên
        // nó đỏ nếu ai đó bỏ lại việc map `prescription_id`.
        var harness = Harness.ForFailedPrediction(Guid.NewGuid());
        harness.Prescription
            .Setup(c => c.PrescribeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ReturnsAsync(new AiPrescriptionResult(
                Prescription: "Kiểm tra cell 3",
                ActionSteps: new[] { "Đo điện áp từng cell" },
                PpeRequired: new[] { "Găng cách điện" },
                SopReferences: Array.Empty<string>(),
                SafetyWarnings: Array.Empty<string>(),
                HumanVerificationRequired: false,
                Enriched: true,
                LlmProvider: "deepseek",
                PrescriptionId: "presc-abc-123"));

        await harness.RunTickAsync();

        harness.Alerts.Should().ContainSingle()
            .Which.AiPrescriptionId.Should().Be("presc-abc-123");
    }

    [Fact]
    public async Task Tick_WhenAiReturnsNoPrescriptionId_AlertKeepsItNull()
    {
        // enrich=false hoặc AI không trả id ⇒ không có gì để học. Bịa ra một id giả sẽ khiến
        // endpoint phản hồi gọi AI với id không tồn tại rồi nhận 410 mãi.
        var harness = Harness.ForFailedPrediction(Guid.NewGuid());

        await harness.RunTickAsync();

        harness.Alerts.Should().ContainSingle().Which.AiPrescriptionId.Should().BeNull();
    }

    private static SensorReading FullReading(Guid assetId, DateTime time, int? cycle, decimal soc) => new()
    {
        Time = time,
        BatteryAssetId = assetId,
        Voltage = 12.4m,
        Current = -1.2m,
        Temperature = 31.0m,
        SocPercent = soc,
        CycleCount = cycle,
        SourceType = SensorReadingSourceTypeEnum.Bms,
    };

    // ── GH-780 ───────────────────────────────────────────────────────────────────────────────
    // `Ai:MinReadings` đóng hai vai xung khắc: vừa là ngưỡng "đủ lịch sử", vừa là số dòng payload.
    // AI từ chối mọi payload khác 30 dòng, nên đặt 29 hay 31 là qua ngưỡng rồi gửi sai hình dạng ⇒
    // prediction DỪNG HẲN mà nhìn cấu hình không thấy gì sai.

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(45)]
    public async Task Tick_WhateverMinReadingsIs_PayloadIsAlwaysExactlyWindowSize(int minReadings)
    {
        // Có dư mẫu tới đâu thì payload vẫn phải đúng 30 dòng — số dòng thuộc về trọng số model,
        // không phải tham số vận hành.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-30);
        var harness = Harness.ForReadings(
            assetId, Window(assetId, t0, minReadings + 10), minReadings: minReadings);

        await harness.RunTickAsync();

        harness.LastReadingsSentToAi.Should().NotBeNull();
        harness.LastReadingsSentToAi!.Should().HaveCount(AiOptions.WindowSize);
        // Và phải là 30 mẫu MỚI NHẤT, không phải 30 mẫu đầu tiên.
        harness.Predictions.Should().ContainSingle()
            .Which.InputWindowEndUtc.Should().Be(t0.AddSeconds(13 * (minReadings + 10 - 1)));
    }

    [Fact]
    public async Task Tick_FewerReadingsThanWindowSize_SkipsWithoutCallingAi()
    {
        // 29 mẫu: dù ngưỡng có đặt thấp tới đâu cũng KHÔNG dựng nổi payload 30 dòng. Gọi AI ở đây
        // là tốn một vòng mạng cho một payload chắc chắn bị từ chối.
        var assetId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-30);
        var harness = Harness.ForReadings(
            assetId, Window(assetId, t0, AiOptions.WindowSize - 1), minReadings: AiOptions.WindowSize);

        await harness.RunTickAsync();

        harness.Predictions.Should().BeEmpty();
        harness.Prediction.Verify(
            c => c.PredictAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// GH-780 — <paramref name="count"/> reading hợp lệ, cách nhau 13 giây, MỚI NHẤT ở cuối.
    /// <paramref name="mutate"/> sửa mẫu theo chỉ số để dựng đúng ca cần thử.
    /// </summary>
    /// <remarks>
    /// Mọi test tầng job phải chạy ở quy mô ≥ 30: AI từ chối payload khác 30 dòng, nên một test
    /// dùng 3-4 mẫu là đang kiểm một cấu hình không bao giờ tồn tại ở production.
    /// </remarks>
    private static SensorReading[] Window(
        Guid assetId, DateTime t0, int count, Action<int, SensorReading>? mutate = null)
    {
        var list = new SensorReading[count];
        for (var i = 0; i < count; i++)
        {
            var r = OutlierAwareReading(assetId, t0.AddSeconds(13 * i), 12.4m);
            mutate?.Invoke(i, r);
            list[i] = r;
        }
        return list;
    }

    private static SensorReading OutlierAwareReading(Guid assetId, DateTime time, decimal voltage) => new()
    {
        Time = time,
        BatteryAssetId = assetId,
        Voltage = voltage,
        Current = -1.2m,
        Temperature = 31.0m,
        SocPercent = 40m,
        // Như AssetReading: asset của harness là LiFePO4, mà bộ LFP (soc_mode="cycle") từ
        // chối payload 4 cột. Cửa sổ LFP thiếu cycle_count không dự đoán được ngoài thực tế,
        // nên để null ở đây là dựng một tình huống không tồn tại. Test nào cần thiếu
        // cycle_count thì tự ghi đè qua tham số `mutate` của Window().
        CycleCount = 150,
        SourceType = SensorReadingSourceTypeEnum.Bms,
    };

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

        public MockUnitOfWorkBuilder Uow { get; private set; } = null!;
        public Mock<IAiPrescriptionClient> Prescription { get; private set; } = null!;
        public Mock<IAiPredictionClient> Prediction { get; private set; } = null!;

        /// <summary>GH-762 — payload THỰC SỰ gửi cho AI ở lần gọi gần nhất (null nếu chưa gọi).</summary>
        public IReadOnlyList<double[]>? LastReadingsSentToAi { get; private set; }

        public List<Alert> Alerts => Uow.Alerts.Object.GetAllAsync().ToList();
        public List<OutboxMessage> Outbox => Uow.OutboxMessages.Object.GetAllAsync().ToList();
        public List<SohPrediction> Predictions => Uow.SohPredictions.Object.GetAllAsync().ToList();

        public static Harness ForFailedPrediction(Guid assetId, params Alert[] existingAlerts)
            => Build(assetId, readings: null, existingAlerts);

        /// <summary>
        /// GH-762 — dựng tick với bộ reading tự chỉ định, để kiểm hành vi khi có số đo ngoài dải.
        /// </summary>
        public static Harness ForReadings(Guid assetId, params SensorReading[] readings)
            => Build(assetId, readings, Array.Empty<Alert>());

        /// <summary>GH-780 — bản cho phép đổi ngưỡng, để thử 30/31/45 mà payload vẫn phải đúng 30.</summary>
        public static Harness ForReadings(Guid assetId, SensorReading[] readings, int minReadings)
            => Build(assetId, readings, Array.Empty<Alert>(), minReadings);

        /// <summary>
        /// Bản cho phép chỉ định soc_mode mà AI khai — thứ quyết định gửi 4 hay 6 cột.
        /// Cần vì hai bộ artifact có hợp đồng KHÁC NHAU cho cùng một cửa sổ thiếu cycle_count.
        /// </summary>
        public static Harness ForReadingsWithHealth(
            Guid assetId, SensorReading[] readings, AiHealthResult? health)
            => Build(assetId, readings, Array.Empty<Alert>(), AiOptions.WindowSize, health);

        private static Harness Build(
            Guid assetId, SensorReading[]? readings, Alert[] existingAlerts,
            int minReadings = AiOptions.WindowSize,
            AiHealthResult? health = null)
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
                .WithSensorReadings(readings ??
                Enumerable.Range(0, AiOptions.WindowSize)
                    .Select(i => AssetReading(assetId, t0.AddSeconds(13 * i)))
                    .ToArray())
                .WithAlerts(existingAlerts);

            var harness = new Harness();

            var prediction = new Mock<IAiPredictionClient>();
            prediction
                .Setup(c => c.PredictAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                    It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IReadOnlyList<double[]> rows, AiPackConfig? pack, CancellationToken _) =>
                {
                    harness.LastReadingsSentToAi = rows;
                    // GH-762 — mock phải TỪ CHỐI giống AI thật, nếu không thì khẳng định "vẫn có
                    // prediction" là vô nghĩa: mock trả kết quả bất kể payload nên test sẽ xanh
                    // ngay cả khi outlier vẫn lọt vào. Dải per-cell [2.0, 4.5] lấy từ
                    // ai-module/src/core/config.py; cố ý viết thẳng số ở đây thay vì gọi
                    // AiReadingWindowFilter, kẻo test chỉ so bộ lọc với chính nó.
                    var nSeries = pack?.NSeries ?? 1;
                    var rejects = rows.Any(r => r[0] / nSeries is < 2.0 or > 4.5);
                    return rejects ? null : FailedPrediction();
                });

            var prescription = new Mock<IAiPrescriptionClient>();
            prescription
                .Setup(c => c.PrescribeAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
                    It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
                .ReturnsAsync((AiPrescriptionResult?)null);

            // Health mock khớp production: bộ NASA/NMC train với soc_mode="window", bộ LFP
            // với soc_mode="cycle". Asset trong harness là LiFePO4 ⇒ vẫn gửi 6 cột như
            // trước, nên mọi khẳng định sẵn có về hình dạng payload không đổi nghĩa.
            var healthClient = new Mock<IAiHealthClient>();
            healthClient
                .Setup(c => c.GetHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(health ?? DefaultHealth());

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IBatteryUnitOfWork))).Returns(uow.Build());
            provider.Setup(p => p.GetService(typeof(IAiPredictionClient))).Returns(prediction.Object);
            provider.Setup(p => p.GetService(typeof(IAiPrescriptionClient))).Returns(prescription.Object);
            provider.Setup(p => p.GetService(typeof(IAiHealthClient))).Returns(healthClient.Object);

            var scope = new Mock<IServiceScope>();
            scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);

            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

            harness.Uow = uow;
            harness.Prescription = prescription;
            harness.Prediction = prediction;
            harness._sut = Make(
                // GH-780 — MinReadings = 3 là cấu hình BẤT KHẢ THI: AI từ chối mọi payload khác 30 dòng.
                // Harness cũ xanh trong khi mã hoá đúng thiết lập mà issue nói là hỏng.
                new AiOptions { Enabled = true, MinReadings = minReadings, MaxScanReadings = minReadings * 2, PrescriptionEnabled = true },
                scopeFactory.Object);
            return harness;
        }

        /// <summary>
        /// Health mặc định — phản ánh đúng bộ artifact đang chạy: NASA/NMC là
        /// <c>soc_mode="window"</c>, LFP là <c>"cycle"</c>.
        /// </summary>
        public static AiHealthResult DefaultHealth(
            string socMode = "window", string lfpSocMode = "cycle", bool lfpLoaded = true)
            => new(
                Status: "ok",
                ModelVersion: "1.6",
                ScalerLoaded: true,
                MambaLoaded: true,
                IsolationForestLoaded: true,
                LfpLoaded: lfpLoaded,
                LfpModelVersion: "2.0-lfp",
                SocMode: socMode,
                LfpSocMode: lfpSocMode,
                LongLoaded: false,
                LongModelVersion: "2.2");

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
            // Asset của harness là LiFePO4, mà bộ artifact LFP train với soc_mode="cycle" ⇒
            // nó TỪ CHỐI THẲNG payload 4 cột. Một cửa sổ LFP thiếu cycle_count là cửa sổ
            // KHÔNG dự đoán được trong thực tế, nên để null ở đây sẽ khiến mọi test dùng
            // harness mặc định chạy trên một tình huống không tồn tại ngoài production.
            CycleCount = 150,
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

    private static IReadOnlyList<double[]> InvokeBuildReadings(
        IReadOnlyList<SensorReading> window, bool allowDerived = true)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("BuildReadings", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IReadOnlyList<double[]>)method.Invoke(null, new object[] { window, allowDerived })!;
    }

    // ── Ngữ cảnh lịch sử gửi kèm Prescribe ────────────────────────────────

    [Fact]
    public async Task Tick_Prescribe_SendsBatteryHistoryContext_AndNeverAgentic()
    {
        // AI nhận age_cycles/last_maintenance_date/ticket_history từ lâu nhưng bridge chưa
        // bao giờ gửi ⇒ LLM luôn kê đơn cho một viên pin "không có quá khứ".
        var assetId = Guid.NewGuid();
        var harness = Harness.ForFailedPrediction(assetId);

        await harness.RunTickAsync();

        harness.Prescription.Verify(
            c => c.PrescribeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<double[]>>(),
                true,
                It.IsAny<AiPackConfig?>(),
                It.IsAny<CancellationToken>(),
                // Phải có ngữ cảnh, và age_cycles phải lấy từ mẫu MỚI NHẤT của cửa sổ.
                It.Is<AiPrescriptionContext?>(x => x != null && x.AgeCycles == 150),
                // agentic PHẢI false ở luồng auto-ticket: nó tốn thêm một lượt LLM trong
                // cùng ngân sách giờ, mà đường này chạy tự động cho mọi pin.
                false),
            Times.Once);
    }

    private static bool InvokeAllowDerivedColumns(string? socMode, string? chemistry)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("AllowDerivedColumns", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object?[] { socMode, chemistry })!;
    }

    // ── soc_percent ↔ soc_mode ────────────────────────────────────────────
    // AI dùng soc_percent AS-IS và KHÔNG kiểm: gửi sai định nghĩa không sinh lỗi nào,
    // nó chỉ dịch SOH đi. Nên đây là hành vi phải được khoá bằng test, không phải
    // thứ để suy luận lúc đọc code.

    [Theory]
    [InlineData("cycle", "LFP", true)]      // bộ LFP hiểu SOC thật của pin
    [InlineData("cycle", "NMC", true)]      // theo bộ artifact, KHÔNG theo chemistry
    [InlineData("window", "LFP", false)]    // bộ này chờ SOC window-local ⇒ đừng gửi SOC thật
    [InlineData("window", "NMC", false)]
    [InlineData("unknown", "LFP", false)]   // artifact khai giá trị lạ ⇒ không đoán
    [InlineData("unknown", "NMC", false)]
    public void AllowDerivedColumns_FollowsSocModeOfTheArtifactSet(
        string socMode, string chemistry, bool expected)
        => Assert.Equal(expected, InvokeAllowDerivedColumns(socMode, chemistry));

    [Theory]
    // Không gọi được Health ⇒ lùi về suy luận theo chemistry. LFP giữ 6 cột vì bộ đó
    // TỪ CHỐI thẳng payload 4 cột — hạ xuống 4 sẽ làm mọi pin LFP mất dự đoán.
    [InlineData("LFP", true)]
    // Còn lại dùng 4 cột: luôn hợp lệ, AI tự tính SOC window-local đúng như lúc train.
    [InlineData("NMC", false)]
    [InlineData(null, false)]
    public void AllowDerivedColumns_WhenHealthUnknown_FallsBackToChemistry(
        string? chemistry, bool expected)
        => Assert.Equal(expected, InvokeAllowDerivedColumns(null, chemistry));

    [Fact]
    public void BuildReadings_WhenDerivedNotAllowed_SendsFourColumns()
    {
        var t0 = DateTime.UtcNow;
        var window = Enumerable.Range(0, AiOptions.WindowSize)
            .Select(i =>
            {
                var r = MakeReading(t0.AddSeconds(10 * i), 3.3m, -1.5m, 25m);
                r.CycleCount = 42;   // dữ liệu ĐỦ, nhưng bộ artifact không hiểu định nghĩa
                return r;
            })
            .ToList();

        var rows = InvokeBuildReadings(window, allowDerived: false);

        Assert.All(rows, r => Assert.Equal(4, r.Length));
    }

    [Fact]
    public void BuildReadings_WhenDerivedAllowed_SendsSixColumns()
    {
        var t0 = DateTime.UtcNow;
        var window = Enumerable.Range(0, AiOptions.WindowSize)
            .Select(i =>
            {
                var r = MakeReading(t0.AddSeconds(10 * i), 3.3m, -1.5m, 25m);
                r.CycleCount = 42;
                return r;
            })
            .ToList();

        var rows = InvokeBuildReadings(window, allowDerived: true);

        Assert.All(rows, r => Assert.Equal(6, r.Length));
    }
}
