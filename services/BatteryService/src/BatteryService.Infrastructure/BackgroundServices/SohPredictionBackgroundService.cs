using System.Text.Json;
using BatteryService.Application.Ai;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using OutboxEntity = BatteryService.Domain.Entities.OutboxMessage;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// BE-AI — job nền gọi AI /predict cho mỗi pin Active (clone WeatherSyncBackgroundService).
/// Mỗi <c>IntervalMinutes</c>: gom 30 reading Bms mới nhất/pin → convert → PredictAsync
/// (gRPC primary → HTTP fallback) → insert SohPrediction + AnomalyClassification.
/// Nếu classification ∈ {Degrading, Failed} → raise Alert (SohDegradation) + Outbox event
/// (tái dùng luồng CreateTicketFromAlert hiện có).
///
/// <see cref="AiOptions.Enabled"/>=false → no-op hoàn toàn (threshold rule vẫn chạy).
/// AI down (cả 2 transport) → PredictAsync trả null → skip pin, KHÔNG làm chết tick.
/// </summary>
public class SohPredictionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiOptions _options;
    private readonly ILogger<SohPredictionBackgroundService> _logger;

    public SohPredictionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AiOptions> options,
        ILogger<SohPredictionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("SohPredictionBackgroundService disabled by config (Ai:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _logger.LogInformation(
            "SohPrediction started (interval={Mins}m, minReadings={Min}, windowSize={Window})",
            _options.IntervalMinutes, _options.MinReadings, AiOptions.WindowSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SohPrediction tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("SohPredictionBackgroundService stopped");
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiPredictionClient>();
        var prescriptionClient = scope.ServiceProvider.GetRequiredService<IAiPrescriptionClient>();
        var healthClient = scope.ServiceProvider.GetRequiredService<IAiHealthClient>();

        // Đọc soc_mode của TỪNG bộ artifact từ chính AI, thay vì suy ra từ chemistry.
        // soc_mode là thuộc tính của BỘ TRỌNG SỐ: mỗi bộ được train với một định nghĩa
        // soc_percent khác nhau, và gửi sai định nghĩa KHÔNG bị AI từ chối — nó chỉ dịch
        // SOH đi. Suy từ chemistry sẽ hỏng lặng lẽ đúng vào ngày một bộ được retrain với
        // định nghĩa kia. Gọi 1 lần mỗi lượt; client tự cache 5 phút.
        var aiHealth = await healthClient.GetHealthAsync(ct);
        if (aiHealth is not null && !aiHealth.LfpLoaded)
        {
            // Không chặn lượt chạy: pin NMC/NCA vẫn dự đoán bình thường. Nhưng phải nói ra,
            // vì mọi pin LiFePO4 sẽ fail ở tầng inference chứ không phải ở đây.
            _logger.LogWarning(
                "AI chưa nạp bộ trọng số LFP (lfp_model_version={Version}) — mọi pin LiFePO4 "
                + "sẽ bị AI từ chối ở tầng inference cho tới khi artifact có mặt.",
                aiHealth.LfpModelVersion);
        }

        var assets = await uow.BatteryAssets
            .GetAllAsync()
            .Where(a => !a.IsDeleted && a.Status == BatteryStatusEnum.Active)
            .Select(a => new
            {
                a.Id,
                a.CustomerId,
                a.SiteId,
                a.SerialNumber,
                // BatteryType — cần để tính pack_config (n_series/chemistry/capacity) cho AI.
                NominalVoltage = a.BatteryType.NominalVoltage,
                NominalCapacityAh = a.BatteryType.NominalCapacityAh,
                Chemistry = a.BatteryType.Chemistry,
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        int predicted = 0, alerts = 0;
        // GH-762 — hai con so chat luong du lieu, gop vao log ket thuc luot.
        int quarantined = 0, degraded = 0;

        foreach (var asset in assets)
        {
            try
            {
                // 30 reading Bms mới nhất của pin (DESC lấy N mới nhất, rồi đảo tăng dần theo Time).
                // Lấy 30 reading mới nhất của pin. Ưu tiên nguồn "primary" (BMS/INA226 chính) khi
                // có nhiều sensor/timestamp; nếu payload không set SensorSourceCode thì lấy tất cả.
                // KHÔNG hardcode SourceType — production data (IoT gateway) có source_type=2, không phải Bms.
                // GH-762 — lấy DƯ mẫu để còn chỗ bù sau khi loại số đo bất khả thi. Trước đây lấy
                // đúng 30: chỉ một outlier là AI từ chối cả cửa sổ, job nhận null rồi `continue`,
                // và pin đó câm lặng cho tới khi outlier tự rơi khỏi 30 mẫu.
                var scanLimit = Math.Max(
                    Math.Max(_options.MinReadings, AiOptions.WindowSize), _options.MaxScanReadings);
                var recentDesc = await uow.SensorReadings
                    .GetAllAsync()
                    .Where(r => r.BatteryAssetId == asset.Id
                                && (r.SensorSourceCode == null
                                    || r.SensorSourceCode == ""
                                    || r.SensorSourceCode == "primary"))
                    .OrderByDescending(r => r.Time)
                    .Take(scanLimit)
                    .ToListAsync(ct);

                if (recentDesc.Count < Math.Max(_options.MinReadings, AiOptions.WindowSize))
                    continue; // chưa đủ reading → skip pin

                var scanned = recentDesc.OrderBy(r => r.Time).ToList(); // tăng dần theo Time
                var packConfig = BuildPackConfig(asset.NominalVoltage, asset.NominalCapacityAh, asset.Chemistry);
                var allowDerived = AllowDerivedColumns(aiHealth?.SocModeFor(packConfig.Chemistry), packConfig.Chemistry);

                // Lọc theo ĐÚNG dải AI chấp nhận, rồi giữ MinReadings mẫu hợp lệ MỚI NHẤT.
                //
                // Lọc trên THỰC THỂ chứ không trên dòng đã dựng: cột `time` gửi cho AI là giây
                // tương đối so với mẫu đầu cửa sổ, nên `BuildReadings` phải chạy SAU khi đã chốt
                // cửa sổ cuối cùng. Dựng trước rồi lọc sẽ khiến cột `time` không bắt đầu từ 0.
                // GH-777 — hàng kiểm phải theo ĐÚNG bố cục sẽ gửi đi, nếu không bộ lọc không thấy
                // cột soc_percent (chỉ số 5) và một SOC xấu lại làm AI từ chối nguyên cửa sổ.
                // Cột `time` ở đây chỉ là chỗ giữ chỗ (0): giá trị thật tính sau khi chốt cửa sổ,
                // và AI không kiểm dải cho `time`.
                var checkRows = scanned
                    .Select(r => allowDerived && r.CycleCount.HasValue
                        ? new[]
                        {
                            (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                            0d, r.CycleCount.Value, (double)r.SocPercent,
                        }
                        : new[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature })
                    .ToList();
                var filtered = AiReadingWindowFilter.Filter(checkRows, packConfig);
                if (filtered.RejectedCount > 0)
                {
                    // Số đo bất khả thi là dấu hiệu hỏng phần cứng / sai mapping cảm biến, không
                    // phải pin xuống cấp. Im lặng ở đây thì không ai biết thiết bị đang gửi rác.
                    _logger.LogWarning(
                        "Data quality: asset {AssetId} ({Serial}) có {Rejected}/{Scanned} số đo ngoài dải AI — "
                        + "đã loại khỏi cửa sổ dự đoán. Dòng đầu tiên: {Reason}",
                        asset.Id, asset.SerialNumber, filtered.RejectedCount, scanned.Count,
                        filtered.FirstRejectionReason);
                    quarantined += filtered.RejectedCount;
                }

                // GH-780 — cần đủ cho CẢ ngưỡng lịch sử LẪN hình dạng payload. Chỉ kiểm ngưỡng
                // thì với MinReadings < 30 sẽ đi tiếp rồi dựng payload thiếu dòng và bị AI từ chối.
                if (filtered.AcceptedCount < Math.Max(_options.MinReadings, AiOptions.WindowSize))
                {
                    // Không đủ mẫu sạch — bỏ qua lượt này, nhưng phải nói rõ vì sao, kẻo lẫn với
                    // "pin mới lắp chưa đủ dữ liệu".
                    if (filtered.RejectedCount > 0)
                    {
                        _logger.LogWarning(
                            "Data quality: asset {AssetId} chỉ còn {Valid}/{Need} số đo hợp lệ sau khi loại — "
                            + "bỏ qua lượt dự đoán này",
                            asset.Id, filtered.AcceptedCount, Math.Max(_options.MinReadings, AiOptions.WindowSize));
                        degraded++;
                    }
                    continue;
                }

                // GH-780 — LUÔN gửi đúng AiOptions.WindowSize dòng, bất kể MinReadings đặt bao nhiêu.
                // 30 là hình dạng nướng vào trọng số model, không phải tham số vận hành.
                // (danh sách tăng dần theo Time nên lấy phần đuôi = mẫu mới nhất)
                var window = filtered.AcceptedIndices
                    .Skip(filtered.AcceptedCount - AiOptions.WindowSize)
                    .Select(i => scanned[i])
                    .ToList();

                // Bộ artifact soc_mode="cycle" TỪ CHỐI THẲNG payload 4 cột — nó cần SOC thật
                // của cả chu kỳ xả, thứ không thể ước lượng từ 30 mẫu. Thiếu cycle_count là
                // cầm chắc bị từ chối, nên gọi đi chỉ tốn một lượt gRPC + một lượt HTTP
                // fallback rồi vẫn về tay không. Dừng sớm và nói rõ nguyên nhân là dữ liệu
                // cảm biến thiếu cột, chứ không phải "AI hỏng".
                if (allowDerived && !window.All(r => r.CycleCount.HasValue))
                {
                    _logger.LogWarning(
                        "Data quality: asset {AssetId} ({Serial}) thiếu cycle_count ở ít nhất 1 "
                        + "trong {Window} mẫu, nhưng bộ artifact cho chemistry={Chemistry} đòi "
                        + "payload 6 cột (soc_mode=cycle) — bỏ qua lượt này thay vì gọi AI để bị từ chối.",
                        asset.Id, asset.SerialNumber, AiOptions.WindowSize, packConfig.Chemistry);
                    degraded++;
                    continue;
                }

                // Dựng dòng SAU khi chốt cửa sổ ⇒ cột `time` bắt đầu từ 0 đúng như hợp đồng.
                var readings = BuildReadings(window, allowDerived);

                var result = await aiClient.PredictAsync(asset.Id.ToString(), readings, packConfig, ct);
                if (result is null)
                    continue; // AI down / input rejected → no-op

                var windowStart = window.First().Time;
                var windowEnd = window.Last().Time;

                // 1. Lưu SohPrediction (lịch sử chart dashboard).
                await uow.SohPredictions.AddAsync(new SohPrediction
                {
                    Id = Guid.NewGuid(),
                    BatteryAssetId = asset.Id,
                    PredictedSohPercent = result.SohPercent,
                    Confidence = result.Confidence,
                    ModelVersion = result.ModelVersion,
                    InputWindowStartUtc = windowStart,
                    InputWindowEndUtc = windowEnd,
                    PredictedAt = now,
                    LatencyMs = result.LatencyMs,
                    // Nguyên văn response AI. Cột này có từ migration đầu tiên nhưng chưa
                    // bao giờ được set, nên ~26 field AI trả về (health_stage,
                    // stage_probabilities, is_borderline, soh_trend, soh_trajectory,
                    // warnings, feature_summary, is_temperature_ood, …) bị mất ngay tại
                    // ranh giới bridge và không cách nào lấy lại.
                    RawResponse = result.RawResponse,
                    // Rút ra cột riêng để dashboard/báo cáo không phải đào jsonb.
                    // RulCyclesEstimate và AiPriority trước đây được PARSE ở cả hai client
                    // rồi vứt ngay — không lưu, không lộ ra đâu cả.
                    HealthStage = result.HealthStage,
                    StageConfidence = result.StageConfidence,
                    IsBorderline = result.IsBorderline,
                    SohStd = result.SohStd,
                    RulCyclesEstimate = result.RulCyclesEstimate,
                    AiPriority = result.Priority,
                    RiskLevel = result.RiskLevel,
                    ActionCode = result.ActionCode,
                    SohTrend = result.SohTrend,
                    DegradationRatePerCycle = result.DegradationRatePerCycle,
                    CyclesToMaintenance = result.CyclesToMaintenance,
                    IsTemperatureOod = result.IsTemperatureOod,
                });

                if (result.IsTemperatureOod)
                {
                    // Model đang ngoại suy ngoài miền nhiệt đã train. Prediction vẫn trả về
                    // và trông hoàn toàn bình thường — không nói ra thì không ai biết con số
                    // này kém tin cậy vì lý do môi trường chứ không phải vì pin.
                    _logger.LogWarning(
                        "Asset {AssetId} ({Serial}): nhiệt độ cửa sổ ngoài miền huấn luyện — "
                        + "SOH {Soh}% là kết quả NGOẠI SUY, độ tin cậy thấp hơn bình thường.",
                        asset.Id, asset.SerialNumber, result.SohPercent);
                }

                // 2. Lưu AnomalyClassification (bằng chứng model chạy + feedback loop retrain).
                await uow.AnomalyClassifications.AddAsync(new AnomalyClassification
                {
                    Id = Guid.NewGuid(),
                    BatteryAssetId = asset.Id,
                    Classification = result.Classification,
                    AnomalyScore = result.AnomalyScore,
                    // Độ tin cậy của PHÂN LOẠI (IsolationForest), KHÔNG phải soh_confidence —
                    // 2 đại lượng khác nhau, xem AiPredictionResult.
                    Confidence = result.AnomalyConfidence,
                    ModelVersion = result.ModelVersion,
                    ClassifiedAt = now,
                    LatencyMs = result.LatencyMs,
                });
                predicted++;

                // 3. Raise Alert nếu pin xuống cấp / hỏng (Failed=Critical, Degrading=Warning).
                if (result.Classification is AnomalyClassificationEnum.Degrading
                    or AnomalyClassificationEnum.Failed)
                {
                    // 3a. Prescription (RAG+LLM, enrich=true) — GH-783: KHÔNG gọi ở đây nữa.
                    //     Truyền xuống dạng delegate và chỉ await ở đúng nhánh ghi Outbox Critical,
                    //     để "prescribe cho alert sắp bị dedup" là bất khả thi về cấu trúc chứ không
                    //     phụ thuộc việc người sửa sau nhớ giữ đúng thứ tự lệnh.
                    //     Best-effort: prescribe fail → prescription = null, ticket vẫn tạo (không block).
                    //     GH-778: trả về CẢ kết quả chứ không chỉ đoạn text. Trước đây
                    //     BuildPrescriptionText nuốt luôn object, nên `prescription_id` chết ngay tại
                    //     đây và không còn đường gửi phản hồi về AI.
                    Func<CancellationToken, Task<AiPrescriptionResult?>> prescribeAsync = async token =>
                    {
                        if (!_options.PrescriptionEnabled)
                            return null;
                        // Ngữ cảnh lịch sử — AI nhận ba field này từ lâu nhưng bridge chưa
                        // bao giờ gửi, nên LLM luôn kê đơn cho một viên pin "không quá khứ".
                        var prescribeContext = await BuildPrescriptionContextAsync(uow, asset.Id, window, token);
                        return await prescriptionClient.PrescribeAsync(
                            asset.Id.ToString(), readings, enrich: true, packConfig, token,
                            // agentic=false: luồng auto-ticket không bật chain 2 lượt LLM.
                            // Nó dành cho thao tác thủ công của kỹ thuật viên (xem C7).
                            context: prescribeContext, agentic: false);
                    };

                    if (await RaiseSohAlertAsync(uow, asset.Id, asset.CustomerId, asset.SiteId,
                            asset.SerialNumber, result, prescribeAsync, now, ct))
                        alerts++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SohPrediction failed for asset {AssetId} — skipping", asset.Id);
            }
        }

        if (predicted > 0)
        {
            await uow.SaveChangesAsync(ct);
            _logger.LogInformation(
                "SohPrediction tick: predicted={Predicted}, alerts={Alerts}, "
                + "quarantinedReadings={Quarantined}, assetsSkippedForDataQuality={Degraded}",
                predicted, alerts, quarantined, degraded);
        }
        else if (quarantined > 0 || degraded > 0)
        {
            // Không pin nào dự đoán được VÀ có số đo bị loại ⇒ nhiều khả năng sự cố cảm biến
            // diện rộng. Nhánh cũ chỉ log khi predicted > 0, nên đúng lúc hỏng nặng nhất thì
            // lượt chạy lại im hoàn toàn.
            _logger.LogWarning(
                "SohPrediction tick: không có prediction nào — quarantinedReadings={Quarantined}, "
                + "assetsSkippedForDataQuality={Degraded}",
                quarantined, degraded);
        }
    }

    /// <summary>
    /// Build payload 30 × <c>[voltage, current, temperature, time]</c> — hoặc
    /// <c>[voltage, current, temperature, time, cycle_count, soc_percent]</c> khi đủ dữ liệu (GH-777).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ time = GIÂY tương đối trong window (<c>reading.Time - window[0].Time</c>), KHÔNG phải
    /// DateTime. decimal → double cho voltage/current/temperature.
    /// </para>
    /// <para>
    /// GH-777 — trước đây luôn chỉ gửi 4 cột, dù <c>SensorReading</c> có sẵn <c>CycleCount</c> và
    /// <c>SocPercent</c> và AI nhận được 6 cột. Hậu quả: AI phải thay cycle bằng 0 và tự ước lượng
    /// SOC từ chính cửa sổ 30 mẫu — một ước lượng cục bộ, kém hẳn số đo thật lấy từ toàn bộ lịch sử
    /// sạc/xả của pin. Đo được cùng seed cùng model: 4 cột ra SOH 67.33, 6 cột (cycle=150, SOC=20)
    /// ra 40.46 — lệch 26.87 điểm. Tức bridge đang vứt bỏ đặc trưng quan trọng ngay trước inference.
    /// </para>
    /// <para>
    /// Chỉ gửi 6 cột khi MỌI mẫu trong cửa sổ đều có <c>CycleCount</c>. Hợp đồng AI đòi cycle và soc
    /// "tất cả hoặc không" (<c>predict.py</c>: "must be set on either all readings or none"), và một
    /// cửa sổ nửa nọ nửa kia sẽ bị từ chối NGUYÊN KHỐI — đúng cái vòng câm lặng mà GH-762 vừa gỡ.
    /// <c>CycleCount</c> là nullable và thực tế có mẫu thiếu (120.762/120.770), nên nhánh 4 cột vẫn
    /// phải giữ, không phải để "phòng hờ".
    /// </para>
    /// </remarks>
    /// <summary>
    /// Có được phép gửi 2 cột suy dẫn (<c>cycle_count</c>, <c>soc_percent</c>) hay không.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>soc_percent</c> mà BE có là SOC THẬT của pin — tính trên toàn bộ lịch sử sạc/xả,
    /// tức "scope theo chu kỳ". Chỉ bộ artifact nào được train với <c>soc_mode="cycle"</c>
    /// mới hiểu đúng con số đó. Bộ train với <c>soc_mode="window"</c> lại chờ SOC
    /// window-local (~100% ở hàng ĐẦU của đúng 30 hàng này) — đưa SOC thật vào là ngoài
    /// phân bố huấn luyện.
    /// </para>
    /// <para>
    /// Và AI KHÔNG kiểm <c>soc_percent</c>: hợp đồng ghi rõ "used AS-IS, no validation —
    /// a wrong soc_percent is a SILENT failure". Không có exception, không có cảnh báo,
    /// chỉ có SOH lệch đi. Nên đây là chỗ phải quyết định đúng, không phải chỗ để đoán.
    /// </para>
    /// <para>
    /// Khi <paramref name="socMode"/> là <c>null</c> (không gọi được Health) thì lùi về suy
    /// luận theo chemistry: LFP giữ 6 cột (bộ đó train <c>soc_mode="cycle"</c> và TỪ CHỐI
    /// thẳng payload 4 cột, nên hạ xuống 4 sẽ làm mọi pin LFP mất dự đoán), còn lại dùng
    /// 4 cột — đường luôn an toàn vì AI tự tính SOC window-local đúng như lúc train.
    /// </para>
    /// </remarks>
    private static bool AllowDerivedColumns(string? socMode, string? chemistry) => socMode switch
    {
        "cycle" => true,
        // "window" và "unknown" đều KHÔNG được gửi SOC thật. "unknown" nghĩa là bộ artifact
        // khai một giá trị build này không hiểu — đoán bừa ở đây là đúng kiểu lỗi im lặng.
        "window" or "unknown" => false,
        _ => string.Equals(chemistry, "LFP", StringComparison.OrdinalIgnoreCase),
    };

    private static IReadOnlyList<double[]> BuildReadings(
        IReadOnlyList<SensorReading> window, bool allowDerived)
    {
        var t0 = window[0].Time;

        // SocPercent không nullable nên luôn có; chỉ CycleCount mới cần kiểm.
        // allowDerived là cổng thứ hai: đủ dữ liệu vẫn chưa đủ, bộ artifact phải hiểu
        // đúng định nghĩa soc_percent thì mới được gửi (xem AllowDerivedColumns).
        var includeDerived = allowDerived && window.All(r => r.CycleCount.HasValue);

        var rows = new List<double[]>(window.Count);
        foreach (var r in window)
        {
            var seconds = (r.Time - t0).TotalSeconds;
            rows.Add(includeDerived
                ? new[]
                {
                    (double)r.Voltage,
                    (double)r.Current,
                    (double)r.Temperature,
                    seconds,
                    r.CycleCount!.Value,
                    (double)r.SocPercent,
                }
                : new[]
                {
                    (double)r.Voltage,
                    (double)r.Current,
                    (double)r.Temperature,
                    seconds,
                });
        }
        return rows;
    }

    /// <summary>
    /// Tính pack_config cho AI từ BatteryType: n_series = nominal_voltage_pack / cell_nominal_voltage.
    /// Pin 12.8V LFP (cell 3.2V) → 4S · 48V NMC (cell 3.7V) → ~13S. AI chia voltage per-cell trước
    /// scaler + range guard, nếu không pack 12V/48V bị reject (range per-cell [2.0, 4.5]V).
    /// </summary>
    private static AiPackConfig BuildPackConfig(
        decimal nominalVoltage, decimal nominalCapacityAh, BatteryChemistryEnum chemistry)
    {
        // Điện áp danh định 1 cell theo chemistry (V).
        var (cellNominal, aiChemistry) = chemistry switch
        {
            BatteryChemistryEnum.LiFePO4 => (3.2m, "LFP"),
            BatteryChemistryEnum.Nmc => (3.7m, "NMC"),
            BatteryChemistryEnum.Nca => (3.6m, "NMC"),  // gần NMC — dùng profile NMC
            BatteryChemistryEnum.Lco => (3.7m, "NMC"),
            _ => (3.7m, (string?)null),                 // Other/unknown → NMC default profile
        };
        var nSeries = Math.Max(1, (int)Math.Round(nominalVoltage / cellNominal));
        return new AiPackConfig(nSeries, aiChemistry, (double)nominalCapacityAh);
    }

    /// <summary>
    /// Dựng ngữ cảnh lịch sử pin cho <c>Prescribe(enrich=true)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nguồn dữ liệu là ALERT ĐÃ RESOLVE của chính BatteryService, không phải ticket của
    /// TicketService: BatteryService không có client sang service đó, và thêm một phụ thuộc
    /// cross-service chỉ để lấy văn bản mô tả là cái giá quá đắt. Alert đã resolve chính là
    /// bản ghi "lần cuối có người đụng vào pin này" mà service này sở hữu.
    /// </para>
    /// <para>
    /// Thứ tự trả về là CŨ → MỚI. Bắt buộc: AI chỉ lấy 5 phần tử CUỐI làm context, nên gửi
    /// ngược chiều đồng nghĩa đưa cho LLM 5 sự kiện xa nhất thay vì gần nhất — sai lệch mà
    /// không có lỗi nào báo ra.
    /// </para>
    /// <para>
    /// Best-effort: lỗi truy vấn ở đây KHÔNG được làm hỏng prescription. Thiếu ngữ cảnh chỉ
    /// làm lời khuyên kém sát, còn ném lỗi thì mất luôn cả prescription.
    /// </para>
    /// </remarks>
    private async Task<AiPrescriptionContext> BuildPrescriptionContextAsync(
        IBatteryUnitOfWork uow, Guid assetId, IReadOnlyList<SensorReading> window, CancellationToken ct)
    {
        try
        {
            // Tuổi pin = cycle_count của mẫu MỚI NHẤT trong cửa sổ (window tăng dần theo Time).
            var ageCycles = window[^1].CycleCount;

            var resolved = await uow.Alerts.GetAllAsync()
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.BatteryAssetId == assetId && a.ResolvedAt != null)
                .OrderByDescending(a => a.ResolvedAt)
                .Take(5)                       // AI chỉ dùng 5 phần tử cuối — lấy dư là phí
                .Select(a => new { a.ResolvedAt, a.AnomalyType, a.Severity })
                .ToListAsync(ct);

            var history = resolved
                .OrderBy(a => a.ResolvedAt)    // đảo lại thành CŨ → MỚI đúng hợp đồng AI
                .Select(a =>
                    $"{a.ResolvedAt!.Value:yyyy-MM-dd}: {a.AnomalyType} ({a.Severity}) — đã xử lý")
                .ToList();

            var lastMaintenance = resolved.Count > 0
                ? resolved.Max(a => a.ResolvedAt)!.Value.ToString("yyyy-MM-dd")
                : null;

            return new AiPrescriptionContext(ageCycles, lastMaintenance, history);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Không dựng được ngữ cảnh lịch sử cho asset {AssetId} — prescribe không ngữ cảnh.",
                assetId);
            return AiPrescriptionContext.Empty;
        }
    }

    /// <summary>Ghép prescription text (mô tả + các bước + PPE) để đổ vào ticket Description.</summary>
    private static string? BuildPrescriptionText(AiPrescriptionResult? presc)
    {
        if (presc is null || string.IsNullOrWhiteSpace(presc.Prescription))
            return null;
        var text = presc.Prescription;
        if (presc.ActionSteps.Count > 0)
            text += "\nSteps:\n" + string.Join("\n", presc.ActionSteps.Select(s => "- " + s));
        if (presc.PpeRequired.Count > 0)
            text += "\nPPE: " + string.Join(", ", presc.PpeRequired);
        // AI trả sẵn điều kiện nên escalate; trước đây bridge bỏ luôn nên Staff không có
        // mốc nào để biết khi nào phải nâng cấp xử lý.
        if (presc.EscalationConditions.Count > 0)
            text += "\nEscalate khi:\n" + string.Join("\n", presc.EscalationConditions.Select(s => "- " + s));
        // Nội dung dưới đây KHÔNG do LLM sinh — nó đã bị safety gate chặn và thay bằng bản
        // rule-based. Không nói ra thì người đọc tưởng đây là khuyến nghị đầy đủ của AI.
        if (presc.Blocked)
            text += "\n⚠ Nội dung LLM bị chặn bởi safety gate — đây là bản rule-based thay thế.";
        if (presc.HumanVerificationRequired)
            text += "\n⚠ Human verification required.";
        return text;
    }

    /// <summary>
    /// Đảm bảo mỗi asset chỉ có ĐÚNG MỘT Alert SohDegradation chưa resolve (GH-783).
    ///
    /// Dedup theo <b>Status</b>, KHÔNG theo <c>DedupWindowEndUtc</c>: window chỉ dài 1 giờ nên
    /// điều kiện cũ <c>DedupWindowEndUtc &gt; now</c> khiến hết giờ là sinh alert mới dù alert cũ
    /// vẫn Open → 188 alert Open trên 9 asset ở E2E, kèm ticket/SLA/notification nhân bản.
    ///
    /// Đã có alert chưa resolve → refresh evidence + window tại chỗ, KHÔNG insert row mới.
    /// Ngoại lệ duy nhất sinh ticket: alert đang mở là Warning (Degrading) mà prediction lên
    /// Failed (Critical) — nâng severity + bắn Outbox một lần, nếu không pin chuyển sang hỏng
    /// sẽ không bao giờ có ticket vì alert Warning đã chiếm chỗ dedup.
    ///
    /// <paramref name="prescribeAsync"/> chỉ được await ở nhánh ghi Outbox Critical → alert bị
    /// dedup KHÔNG tốn RAG/LLM cost. Trả true nếu có ticket event được ghi.
    /// </summary>
    private static async Task<bool> RaiseSohAlertAsync(
        IBatteryUnitOfWork uow, Guid assetId, Guid customerId, Guid? siteId, string serial,
        AiPredictionResult result, Func<CancellationToken, Task<AiPrescriptionResult?>> prescribeAsync,
        DateTime now, CancellationToken ct)
    {
        var severity = result.Classification == AnomalyClassificationEnum.Failed
            ? AlertSeverityEnum.Critical
            : AlertSeverityEnum.Warning;

        var existing = await uow.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.BatteryAssetId == assetId
                        && a.AnomalyType == AnomalyTypeEnum.SohDegradation
                        && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged))
            .OrderByDescending(a => a.DetectedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            // TicketId != null → alert này đã sinh ticket rồi, không bắn event lần hai.
            var escalating = severity == AlertSeverityEnum.Critical
                             && existing.Severity != AlertSeverityEnum.Critical
                             && existing.TicketId is null;

            // ⚠️ KHÔNG refresh DetectedAt mỗi tick: AlertEscalationService lọc alert cần escalate
            // bằng `DetectedAt <= now - EscalationAfterMinutes` (5 phút), mà job này chạy mỗi
            // IntervalMinutes (cũng 5 phút) → đẩy DetectedAt tiến lên liên tục thì alert không bao
            // giờ đủ già để escalate. DetectedAt = "lần đầu phát hiện", không phải "lần cuối thấy".
            existing.ActualValue = result.SohPercent;
            existing.DedupWindowEndUtc = now.AddHours(1);
            if (escalating)
            {
                existing.Severity = AlertSeverityEnum.Critical;
                // Đúng một lần trong vòng đời alert (tick sau Severity đã Critical → escalating=false),
                // nên không tái lập vòng lặp trên. Cho giai đoạn Critical một mốc đếm SLA escalation
                // riêng, giống hệt alert Critical vừa được tạo mới.
                existing.DetectedAt = now;
            }

            uow.Alerts.UpdateAsync(existing);

            if (!escalating)
                return false;

            var existingPresc = await prescribeAsync(ct);
            existing.AiPrescriptionId = existingPresc?.PrescriptionId ?? existing.AiPrescriptionId;
            await WriteCriticalOutboxAsync(
                uow, existing, customerId, serial, BuildPrescriptionText(existingPresc), now);
            return true;
        }

        var alert = new AlertEntity
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = assetId,
            SiteId = siteId,
            AnomalyType = AnomalyTypeEnum.SohDegradation,
            Severity = severity,
            ThresholdValue = 80m, // EOL threshold — SOH < 80% = Failed
            ActualValue = result.SohPercent,
            Unit = "%",
            DetectedAt = now,
            Status = AlertStatusEnum.Open,
            DedupWindowEndUtc = now.AddHours(1),
        };
        await uow.Alerts.AddAsync(alert);

        // Chỉ Critical (Failed) mới bắn event tạo ticket — khớp convention threshold engine.
        if (severity != AlertSeverityEnum.Critical)
            return true;

        var presc = await prescribeAsync(ct);
        alert.AiPrescriptionId = presc?.PrescriptionId;
        await WriteCriticalOutboxAsync(uow, alert, customerId, serial, BuildPrescriptionText(presc), now);
        return true;
    }

    /// <summary>
    /// Ghi cặp Outbox V1 + V2 cho alert Critical — dùng chung cho nhánh tạo mới và nhánh
    /// escalate Degrading → Failed, để hai nhánh không lệch nhau về thứ tự/nội dung event.
    /// </summary>
    private static async Task WriteCriticalOutboxAsync(
        IBatteryUnitOfWork uow, AlertEntity alert, Guid customerId, string serial,
        string? prescriptionText, DateTime now)
    {
        var v1 = new BatteryAnomalyDetectedEvent(
            AlertId: alert.Id,
            BatteryAssetId: alert.BatteryAssetId ?? Guid.Empty,
            CustomerId: customerId,
            AssetSerialNumber: serial,
            AnomalyType: (int)AnomalyTypeEnum.SohDegradation,
            Severity: (int)alert.Severity,
            ThresholdValue: alert.ThresholdValue ?? 0m,
            ActualValue: alert.ActualValue ?? 0m,
            Unit: alert.Unit ?? "%",
            DetectedAt: alert.DetectedAt,
            AnomalyTypeName: nameof(AnomalyTypeEnum.SohDegradation),
            SeverityName: alert.Severity.ToString());
        await uow.OutboxMessages.AddAsync(new OutboxEntity
        {
            Id = Guid.NewGuid(),
            AggregateId = alert.Id,
            Type = nameof(BatteryAnomalyDetectedEvent),
            Payload = JsonSerializer.Serialize(v1),
            // V1 sau V2 1ms: relay ORDER BY OccurredAtUtc → V2 (có AiPrescription) tới saga
            // TRƯỚC → saga Initially hydrate từ V2 → ticket có prescription. V1 tới sau bị
            // skip (saga đã có instance cùng AlertId). Giữ V1 cho consumer khác còn subscribe V1.
            OccurredAtUtc = now.AddMilliseconds(1),
        });

        var v2 = new BatteryAnomalyDetectedV2Event(
            AlertId: alert.Id,
            BatteryAssetId: alert.BatteryAssetId ?? Guid.Empty,
            CustomerId: customerId,
            SiteId: alert.SiteId,
            AssetSerialNumber: serial,
            AnomalyType: (int)AnomalyTypeEnum.SohDegradation,
            Severity: (int)alert.Severity,
            ThresholdValue: alert.ThresholdValue ?? 0m,
            ActualValue: alert.ActualValue ?? 0m,
            Unit: alert.Unit ?? "%",
            DetectedAt: alert.DetectedAt,
            InternalResistanceMilliohm: null,
            CellVoltageDeltaMv: null,
            EnvironmentalIncidentId: null,
            AiPrescription: prescriptionText,
            AiActionSteps: null);
        await uow.OutboxMessages.AddAsync(new OutboxEntity
        {
            Id = Guid.NewGuid(),
            AggregateId = alert.Id,
            Type = nameof(BatteryAnomalyDetectedV2Event),
            Payload = JsonSerializer.Serialize(v2),
            OccurredAtUtc = now,
        });
    }
}
