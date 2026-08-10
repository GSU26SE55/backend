using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2, Q12=A — spec §30.3) — kết quả dự đoán SOH của LSTM/CNN-LSTM cho 1 asset.
/// Lưu lịch sử prediction (score/confidence/latency/modelVersion) để chart lên dashboard + đối chiếu
/// chất lượng giữa các model version. Insert flow do Sprint AI làm.
/// </summary>
public class SohPrediction : AuditableEntity
{
    public Guid BatteryAssetId { get; set; }

    /// <summary>SOH% dự đoán (0–100).</summary>
    public decimal PredictedSohPercent { get; set; }

    public decimal Confidence { get; set; }

    /// <summary>"1.0" / "1.1".</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public DateTime InputWindowStartUtc { get; set; }
    public DateTime InputWindowEndUtc { get; set; }

    /// <summary>Indexed DESC — lấy prediction mới nhất per asset.</summary>
    public DateTime PredictedAt { get; set; }

    public int LatencyMs { get; set; }

    /// <summary>Raw response AI (jsonb) — debug.</summary>
    public string? RawResponse { get; set; }

    // ── Cột rút từ response AI ────────────────────────────────────────────
    // RawResponse giữ nguyên văn để không mất gì, nhưng truy vấn jsonb cho dashboard/báo cáo
    // thì chậm và khó index. Những field đọc thường xuyên được tách ra cột riêng.
    // TẤT CẢ đều nullable: bản ghi trước migration không có dữ liệu này và không suy ngược
    // được, nên NOT NULL sẽ buộc phải bịa ra một giá trị mặc định sai sự thật.

    /// <summary>"Healthy" / "Degrading" / "Maintenance Required" / "End Of Life" (GH-86).</summary>
    public string? HealthStage { get; set; }

    /// <summary>Tỉ lệ mẫu MC Dropout rơi vào <see cref="HealthStage"/>, 0–1.</summary>
    public decimal? StageConfidence { get; set; }

    /// <summary>Kết luận sát ngưỡng — không stage nào chiếm đa số rõ (&lt; 0.7).</summary>
    /// <remarks>
    /// Cần khi đọc chart: pin borderline nhảy qua lại giữa hai stage ở hai lượt liên tiếp là
    /// bình thường. Thiếu cờ này thì trông như pin vừa xấu đi đột ngột.
    /// </remarks>
    public bool IsBorderline { get; set; }

    /// <summary>Độ lệch chuẩn MC Dropout, tính bằng ĐIỂM SOH (không phải [0,1]).</summary>
    public decimal? SohStd { get; set; }

    /// <summary>Ước lượng số chu kỳ còn lại tới EOL (SOH 80%).</summary>
    public int? RulCyclesEstimate { get; set; }

    /// <summary>"P1"/"P2"/"P3"/"None" — tín hiệu URGENCY do AI đề xuất.</summary>
    /// <remarks>
    /// ⚠️ KHÔNG phải Priority của ticket. AI không biết ImpactScope; Priority thật do BE tính
    /// từ ma trận Impact × Urgency lúc Manager triage.
    /// </remarks>
    public string? AiPriority { get; set; }

    /// <summary>"Critical" / "High" / "Medium" / "Low".</summary>
    public string? RiskLevel { get; set; }

    /// <summary>MONITOR / SCHEDULE_MAINTENANCE / SCHEDULE_REPLACEMENT / REPLACE_IMMEDIATELY.</summary>
    public string? ActionCode { get; set; }

    /// <summary>"accelerating" / "stable" / "slowing".</summary>
    public string? SohTrend { get; set; }

    /// <summary>%SOH mất mỗi chu kỳ sạc-xả.</summary>
    public decimal? DegradationRatePerCycle { get; set; }

    /// <summary>Số chu kỳ ước tính tới ngưỡng bảo trì 85%. 0 nếu đã dưới ngưỡng.</summary>
    public int? CyclesToMaintenance { get; set; }

    /// <summary>GH-91 — nhiệt độ ngoài mọi buồng nhiệt lúc train ⇒ model đang NGOẠI SUY.</summary>
    /// <remarks>
    /// Prediction vẫn trả bình thường, không có cảnh báo nào khác. Đây là tín hiệu duy nhất
    /// cho biết SOH kém tin cậy vì miền dữ liệu chứ không phải vì pin xấu.
    /// </remarks>
    public bool IsTemperatureOod { get; set; }

    public BatteryAsset? BatteryAsset { get; set; }
}
