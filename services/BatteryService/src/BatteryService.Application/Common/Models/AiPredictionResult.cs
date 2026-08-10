using BatteryService.Domain.Enums;

namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — pack→cell normalization gửi kèm khi pin là multi-cell pack (GH-65/67).
/// AI chia voltage cho <see cref="NSeries"/> per-cell trước scaler + range guard, nếu không
/// pack 12V/48V bị reject (per-cell range [2.0, 4.5]V). Tính từ BatteryType.NominalVoltage.
/// </summary>
public class AiPackConfig
{
    public AiPackConfig(int NSeries, string? Chemistry, double? CapacityAh)
    {
        this.NSeries = NSeries;
        this.Chemistry = Chemistry;
        this.CapacityAh = CapacityAh;
    }

    public int NSeries { get; }
    public string? Chemistry { get; }
    public double? CapacityAh { get; }
}

/// <summary>
/// BE-AI — kết quả /predict (hoặc gRPC Predict) đã map về domain BE, transport-neutral.
/// gRPC client và HTTP client cùng trả type này → job không quan tâm đi đường nào.
///
/// Field map từ AI response (xem docs/overall-ai-be-integration.md §6.1):
///   SohPercent            ← soh_percent
///   Confidence            ← prediction.soh_confidence (MC Dropout, [0,1]) — độ tin cậy SOH
///   Classification        ← classification "Normal"/"Degrading"/"Failed"
///   AnomalyScore          ← anomaly_score (IsolationForest decision_function)
///   AnomalyConfidence     ← anomaly.anomaly_confidence (|IsolationForest score|, [0,1]) — độ tin cậy phân loại
///   RulCyclesEstimate     ← rul_cycles_estimate
///   Priority              ← risk.priority "P1"/"P2"/"P3"/"None" (chỉ Urgency, KHÔNG phải ticket Priority)
///   ModelVersion          ← metadata.model_version ("1.6")
///   LatencyMs             ← inference_ms
///
/// ⚠️ <see cref="Confidence"/> và <see cref="AnomalyConfidence"/> là 2 đại lượng KHÁC NHAU:
/// SohPrediction lưu <see cref="Confidence"/>, AnomalyClassification lưu <see cref="AnomalyConfidence"/>.
/// Field phẳng <c>confidence</c> của AI response = soh_confidence (xem protos/ai_service.proto:161)
/// — KHÔNG dùng nó cho classification.
/// </summary>
public class AiPredictionResult
{
    public AiPredictionResult(
        decimal SohPercent,
        decimal Confidence,
        AnomalyClassificationEnum Classification,
        decimal AnomalyScore,
        decimal AnomalyConfidence,
        int RulCyclesEstimate,
        string Priority,
        string ModelVersion,
        int LatencyMs,
        string? RawResponse = null,
        string? HealthStage = null,
        decimal? StageConfidence = null,
        bool IsBorderline = false,
        decimal? SohStd = null,
        string? RiskLevel = null,
        string? ActionCode = null,
        string? SohTrend = null,
        decimal? DegradationRatePerCycle = null,
        int? CyclesToMaintenance = null,
        bool IsTemperatureOod = false)
    {
        this.HealthStage = HealthStage;
        this.StageConfidence = StageConfidence;
        this.IsBorderline = IsBorderline;
        this.SohStd = SohStd;
        this.RiskLevel = RiskLevel;
        this.ActionCode = ActionCode;
        this.SohTrend = SohTrend;
        this.DegradationRatePerCycle = DegradationRatePerCycle;
        this.CyclesToMaintenance = CyclesToMaintenance;
        this.IsTemperatureOod = IsTemperatureOod;
        this.RawResponse = RawResponse;
        this.SohPercent = SohPercent;
        this.Confidence = Confidence;
        this.Classification = Classification;
        this.AnomalyScore = AnomalyScore;
        this.AnomalyConfidence = AnomalyConfidence;
        this.RulCyclesEstimate = RulCyclesEstimate;
        this.Priority = Priority;
        this.ModelVersion = ModelVersion;
        this.LatencyMs = LatencyMs;
    }

    public decimal SohPercent { get; }
    public decimal Confidence { get; }
    public AnomalyClassificationEnum Classification { get; }
    public decimal AnomalyScore { get; }
    public decimal AnomalyConfidence { get; }
    public int RulCyclesEstimate { get; }
    public string Priority { get; }
    public string ModelVersion { get; }
    public int LatencyMs { get; }

    // ── GH-86 bất định ────────────────────────────────────────────────────
    // AI chấm ngưỡng bằng median của 10 mẫu MC Dropout. Ba field dưới đây nói cho
    // caller biết kết luận đó CHẮC tới đâu — thứ mà một con số SOH đơn lẻ không nói được.

    /// <summary>"Healthy" / "Degrading" / "Maintenance Required" / "End Of Life".</summary>
    public string? HealthStage { get; }

    /// <summary>Tỉ lệ mẫu MC rơi vào <see cref="HealthStage"/> đã chọn, 0–1.</summary>
    public decimal? StageConfidence { get; }

    /// <summary>
    /// <c>true</c> khi không stage nào chiếm đa số rõ (&lt; 0.7) — kết luận nằm sát ngưỡng.
    /// </summary>
    /// <remarks>
    /// Quan trọng ở mốc EOL 80%: một pin borderline có thể nhảy qua lại giữa hai stage
    /// ở hai lượt chạy liên tiếp mà không có gì bất thường. Không có cờ này thì Staff
    /// thấy stage đổi và tưởng pin vừa xấu đi.
    /// </remarks>
    public bool IsBorderline { get; }

    /// <summary>Độ lệch chuẩn MC Dropout, tính bằng điểm SOH (không phải [0,1]).</summary>
    public decimal? SohStd { get; }

    // ── Rủi ro & xu hướng ─────────────────────────────────────────────────

    /// <summary>"Critical" / "High" / "Medium" / "Low" — hiển thị mức nghiêm trọng.</summary>
    public string? RiskLevel { get; }

    /// <summary>MONITOR / SCHEDULE_MAINTENANCE / SCHEDULE_REPLACEMENT / REPLACE_IMMEDIATELY.</summary>
    public string? ActionCode { get; }

    /// <summary>"accelerating" / "stable" / "slowing" — vận tốc suy giảm.</summary>
    public string? SohTrend { get; }

    /// <summary>%SOH mất đi mỗi chu kỳ sạc-xả, quan sát từ cửa sổ.</summary>
    public decimal? DegradationRatePerCycle { get; }

    /// <summary>Số chu kỳ ước tính tới khi SOH chạm ngưỡng bảo trì 85%. 0 nếu đã dưới.</summary>
    public int? CyclesToMaintenance { get; }

    /// <summary>
    /// GH-91 — <c>true</c> khi nhiệt độ cửa sổ nằm quá xa mọi buồng nhiệt lúc train,
    /// tức model đang NGOẠI SUY.
    /// </summary>
    /// <remarks>
    /// Prediction vẫn trả về bình thường và không có cảnh báo nào khác. Đây là tín hiệu
    /// duy nhất cho biết con số SOH đó kém tin cậy vì lý do miền dữ liệu, chứ không phải
    /// vì pin xấu.
    /// </remarks>
    public bool IsTemperatureOod { get; }

    /// <summary>
    /// Nguyên văn JSON response của AI — đổ vào cột <c>soh_predictions.raw_response</c> (jsonb).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cột này có từ migration đầu tiên nhưng CHƯA BAO GIỜ được ghi: chỗ tạo
    /// <c>new SohPrediction { ... }</c> không set nó, nên nó luôn NULL.
    /// </para>
    /// <para>
    /// Hậu quả không nhỏ: AI trả về ~35 field, class này chỉ mang 9. Toàn bộ phần còn lại
    /// (<c>health_stage</c>, <c>stage_probabilities</c>, <c>is_borderline</c>, <c>soh_trend</c>,
    /// <c>soh_trajectory</c>, <c>degradation_rate_per_cycle</c>, <c>warnings</c>,
    /// <c>feature_summary</c>, <c>is_temperature_ood</c>, …) bị vứt ngay tại ranh giới bridge
    /// và KHÔNG cách nào lấy lại — muốn phân tích lại phải chạy inference lại trên dữ liệu cũ.
    /// Giữ nguyên văn ở đây là cách rẻ nhất để không mất chúng, mà không phải thêm cột cho
    /// từng field một.
    /// </para>
    /// <para>
    /// <c>null</c> khi client không dựng được (không nên xảy ra) — cột nullable nên vẫn ghi được.
    /// </para>
    /// </remarks>
    public string? RawResponse { get; }

    /// <summary>Map chuỗi classification của AI → enum BE (Normal=1/Degrading=2/Failed=3).</summary>
    public static AnomalyClassificationEnum ParseClassification(string? raw) => raw?.Trim() switch
    {
        "Normal" => AnomalyClassificationEnum.Normal,
        "Degrading" => AnomalyClassificationEnum.Degrading,
        "Failed" => AnomalyClassificationEnum.Failed,
        // Fallback an toàn: không rõ → Normal (không tự tạo Alert từ giá trị lạ).
        _ => AnomalyClassificationEnum.Normal,
    };
}
