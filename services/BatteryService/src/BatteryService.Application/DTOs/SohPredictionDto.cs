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
}
