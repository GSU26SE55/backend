namespace BatteryService.Application.DTOs;

/// <summary>BE-AI — DTO trả về SohPrediction (lịch sử dự đoán SOH cho chart dashboard).</summary>
public class SohPredictionDto
{
    /// <summary>ID bản ghi prediction (GUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID BatteryAsset được dự đoán (GUID).</summary>
    public string BatteryAssetId { get; set; } = string.Empty;

    /// <summary>SOH% dự đoán (0–100).</summary>
    public decimal PredictedSohPercent { get; set; }

    /// <summary>Độ tự tin của model, 0–1 (MC Dropout).</summary>
    public decimal Confidence { get; set; }

    /// <summary>Phiên bản model ("1.6").</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Thời điểm AI dự đoán (UTC) — dùng làm trục thời gian chart.</summary>
    public DateTime PredictedAt { get; set; }

    /// <summary>Độ trễ inference (ms).</summary>
    public int LatencyMs { get; set; }

    // ── Bất định (GH-86) ──────────────────────────────────────────────────
    // Một con số SOH đơn lẻ không nói được nó chắc tới đâu. Ba field dưới đây cho FE vẽ
    // dải tin cậy và biết khi nào KHÔNG nên hiển thị kết luận như một sự thật đã chốt.

    /// <summary>"Healthy" / "Degrading" / "Maintenance Required" / "End Of Life".</summary>
    public string? HealthStage { get; set; }

    /// <summary>Tỉ lệ mẫu MC Dropout rơi vào <see cref="HealthStage"/>, 0–1.</summary>
    public decimal? StageConfidence { get; set; }

    /// <summary>Kết luận sát ngưỡng (&lt; 0.7) — FE nên hiển thị kèm cảnh báo.</summary>
    /// <remarks>
    /// Pin borderline nhảy giữa hai stage ở hai lượt liên tiếp là bình thường. Không có cờ
    /// này thì người xem chart tưởng pin vừa xấu đi đột ngột.
    /// </remarks>
    public bool IsBorderline { get; set; }

    /// <summary>Độ lệch chuẩn MC Dropout theo ĐIỂM SOH — dùng vẽ error bar.</summary>
    public decimal? SohStd { get; set; }

    // ── Rủi ro & xu hướng ─────────────────────────────────────────────────

    /// <summary>Số chu kỳ còn lại ước tính tới EOL (SOH 80%).</summary>
    public int? RulCyclesEstimate { get; set; }

    /// <summary>"P1"/"P2"/"P3"/"None" — tín hiệu URGENCY của AI.</summary>
    /// <remarks>⚠️ KHÔNG phải Priority ticket; Priority thật do BE tính từ Impact × Urgency.</remarks>
    public string? AiPriority { get; set; }

    /// <summary>"Critical" / "High" / "Medium" / "Low".</summary>
    public string? RiskLevel { get; set; }

    /// <summary>MONITOR / SCHEDULE_MAINTENANCE / SCHEDULE_REPLACEMENT / REPLACE_IMMEDIATELY.</summary>
    public string? ActionCode { get; set; }

    /// <summary>"accelerating" / "stable" / "slowing".</summary>
    public string? SohTrend { get; set; }

    /// <summary>%SOH mất mỗi chu kỳ sạc-xả.</summary>
    public decimal? DegradationRatePerCycle { get; set; }

    /// <summary>Số chu kỳ tới ngưỡng bảo trì 85%. 0 nếu đã dưới ngưỡng.</summary>
    public int? CyclesToMaintenance { get; set; }

    /// <summary>GH-91 — model đang NGOẠI SUY ngoài miền nhiệt đã train.</summary>
    /// <remarks>
    /// KHÔNG phải dấu hiệu pin xấu, mà là dấu hiệu con số kém tin cậy vì môi trường đo.
    /// FE phải phân biệt rõ hai chuyện này, nếu không người dùng đọc một cảnh báo kỹ thuật
    /// thành một cảnh báo hỏng hóc.
    /// </remarks>
    public bool IsTemperatureOod { get; set; }
}
