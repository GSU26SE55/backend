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
        string? RiskLevel = null,
        string? ActionCode = null,
        IReadOnlyList<AiWarningItem>? Warnings = null)
    {
        this.SohPercent = SohPercent;
        this.Confidence = Confidence;
        this.Classification = Classification;
        this.AnomalyScore = AnomalyScore;
        this.AnomalyConfidence = AnomalyConfidence;
        this.RulCyclesEstimate = RulCyclesEstimate;
        this.Priority = Priority;
        this.ModelVersion = ModelVersion;
        this.LatencyMs = LatencyMs;
        this.RiskLevel = RiskLevel;
        this.ActionCode = ActionCode;
        this.Warnings = Warnings ?? Array.Empty<AiWarningItem>();
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

    /// <summary>GH-805 — <c>risk.risk_level</c>: "Critical" / "High" / "Medium" / "Low".</summary>
    public string? RiskLevel { get; }

    /// <summary>GH-805 — <c>risk.action_code</c>, đưa vào AiEvidence để Staff biết cần làm gì.</summary>
    public string? ActionCode { get; }

    /// <summary>GH-805 — <c>warnings[]</c>. Rỗng nếu AI không trả (response cũ) — không bao giờ null.</summary>
    public IReadOnlyList<AiWarningItem> Warnings { get; }

    /// <summary>Map chuỗi classification của AI → enum BE (Normal=1/Degrading=2/Failed=3).</summary>
    public static AnomalyClassificationEnum ParseClassification(string? raw) => raw?.Trim() switch
    {
        "Normal" => AnomalyClassificationEnum.Normal,
        "Degrading" => AnomalyClassificationEnum.Degrading,
        "Failed" => AnomalyClassificationEnum.Failed,
        // Fallback an toàn: không rõ → Normal (không tự tạo Alert từ giá trị lạ).
        _ => AnomalyClassificationEnum.Normal,
    };

    /// <summary>
    /// GH-805 — severity của Alert, gộp HAI nguồn tín hiệu độc lập: classification và
    /// <c>risk.priority</c>. AI có thể trả Normal kèm priority P1 (VD nhiệt 50°C: SOH vẫn 95%
    /// nhưng TEMP_CRITICAL) — chỉ xét classification thì sự cố đó không bao giờ sinh alert.
    ///
    /// Lấy mức CAO HƠN giữa hai nguồn: Failed không bị P2/P3 hạ xuống Warning, và Degrading
    /// được P1 nâng lên Critical. Trả về đúng MỘT severity nên chỉ có một nhánh raise alert —
    /// không thể duplicate với nhánh classification.
    ///
    /// null = không có tín hiệu nào → KHÔNG raise alert (Normal + P3/None: hành vi cũ).
    /// </summary>
    public static AlertSeverityEnum? ResolveSeverity(
        AnomalyClassificationEnum classification, string? priority)
    {
        var byClassification = classification switch
        {
            AnomalyClassificationEnum.Failed => (AlertSeverityEnum?)AlertSeverityEnum.Critical,
            AnomalyClassificationEnum.Degrading => AlertSeverityEnum.Warning,
            _ => null,
        };

        // P3/None/rỗng KHÔNG phải tín hiệu raise — chỉ P1/P2 (contract docs).
        var byRisk = priority?.Trim().ToUpperInvariant() switch
        {
            "P1" => (AlertSeverityEnum?)AlertSeverityEnum.Critical,
            "P2" => AlertSeverityEnum.Warning,
            _ => null,
        };

        if (byClassification is null && byRisk is null)
        {
            return null;
        }

        if (byClassification == AlertSeverityEnum.Critical || byRisk == AlertSeverityEnum.Critical)
        {
            return AlertSeverityEnum.Critical;
        }

        return AlertSeverityEnum.Warning;
    }

    /// <summary>
    /// GH-805 — suy AnomalyType từ <c>warnings[]</c> thay vì hardcode SohDegradation.
    ///
    /// Lý do: TicketService map AnomalyType → (ImpactScope, Urgency). SohDegradation →
    /// (SingleAsset, Low) → ticket P3 / SLA 72h. Một sự cố nhiệt P1 gán nhầm SohDegradation
    /// sẽ nhận SLA 72h thay vì được xử lý khẩn.
    ///
    /// Ưu tiên warning severity="critical" đầu tiên; không có thì phần tử đầu (deterministic,
    /// không đoán "cái nào nặng hơn"). Code lạ / không có warning → SohDegradation (hành vi cũ).
    /// </summary>
    public static AnomalyTypeEnum MapWarningToAnomalyType(IReadOnlyList<AiWarningItem>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return AnomalyTypeEnum.SohDegradation;
        }

        var warning = warnings.FirstOrDefault(
                          w => string.Equals(w.Severity?.Trim(), "critical", StringComparison.OrdinalIgnoreCase))
                      ?? warnings[0];

        // Code lấy từ ai_service.proto:80-84 (WarningItem.code).
        return warning.Code?.Trim().ToUpperInvariant() switch
        {
            "TEMP_CRITICAL" or "TEMP_HIGH" => AnomalyTypeEnum.Overheat,
            "TEMP_LOW" => AnomalyTypeEnum.Undertemp,
            "VOLTAGE_HIGH" => AnomalyTypeEnum.Overvoltage,
            "VOLTAGE_LOW" => AnomalyTypeEnum.Undervoltage,
            "SOH_LOW" => AnomalyTypeEnum.SohDegradation,
            _ => AnomalyTypeEnum.SohDegradation,
        };
    }
}
