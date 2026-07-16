using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2, Q12=A — spec §30.3) — kết quả phân loại AI cho 1 alert/asset.
/// Bảng RIÊNG (không nhét vào <see cref="Alert"/>) để giữ bằng chứng model chạy thật (score/confidence/
/// latency/modelVersion) + feedback loop cho retrain. Insert flow do Sprint AI (aibeiotrealtime.md) làm;
/// NS-26 dựng persistence + endpoint feedback.
/// </summary>
public class AnomalyClassification : AuditableEntity
{
    /// <summary>Link Alert nếu classify cho 1 alert cụ thể (nullable).</summary>
    public Guid? AlertId { get; set; }

    public Guid BatteryAssetId { get; set; }

    /// <summary>Output: Normal / Degrading / Failed (Isolation Forest + LSTM).</summary>
    public AnomalyClassificationEnum Classification { get; set; }

    /// <summary>Isolation Forest decision score (âm = bất thường hơn).</summary>
    public decimal AnomalyScore { get; set; }

    public decimal Confidence { get; set; }

    /// <summary>"1.0" / "1.1" — khớp artifact versioning.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public DateTime ClassifiedAt { get; set; }

    /// <summary>Monitoring SLA inference &lt; 100ms.</summary>
    public int LatencyMs { get; set; }

    // ── Feedback loop (§30.3 / §30.12) ──
    /// <summary>Staff confirm sau resolve — null khi chưa có feedback.</summary>
    public StaffFeedbackEnum? StaffFeedback { get; set; }
    public Guid? StaffFeedbackByUserId { get; set; }
    public DateTime? StaffFeedbackAt { get; set; }

    public Alert? Alert { get; set; }
    public BatteryAsset? BatteryAsset { get; set; }
}
