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

    public BatteryAsset? BatteryAsset { get; set; }
}
