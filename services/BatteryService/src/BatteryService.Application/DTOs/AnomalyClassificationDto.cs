using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

/// <summary>Sprint Bonus NS-26 (#666, F2) — DTO trả về AnomalyClassification (spec §30.3).</summary>
public class AnomalyClassificationDto
{
    /// <summary>ID bản ghi classification (GUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID Alert liên quan (GUID). <c>null</c> nếu classify không gắn Alert cụ thể.</summary>
    public string? AlertId { get; set; }

    /// <summary>ID BatteryAsset được phân loại (GUID).</summary>
    public string BatteryAssetId { get; set; } = string.Empty;

    /// <summary>Kết quả phân loại (<c>AnomalyClassificationEnum</c>): 1 Normal · 2 Degrading · 3 Failed.</summary>
    public AnomalyClassificationEnum Classification { get; set; }

    /// <summary>Điểm Isolation Forest (âm = bất thường hơn). Precision (8,6).</summary>
    public decimal AnomalyScore { get; set; }

    /// <summary>Độ tự tin của model, 0–1. Precision (4,3).</summary>
    public decimal Confidence { get; set; }

    /// <summary>Phiên bản model ("1.0"/"1.1") — khớp artifact versioning.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Thời điểm AI phân loại (UTC).</summary>
    public DateTime ClassifiedAt { get; set; }

    /// <summary>Độ trễ inference (ms) — monitor SLA inference &lt; 100ms.</summary>
    public int LatencyMs { get; set; }

    /// <summary>
    /// Đánh giá của Staff (<c>StaffFeedbackEnum</c>): 1 Correct · 2 FalsePositive · 3 FalseNegative.
    /// <c>null</c> khi chưa có feedback.
    /// </summary>
    public StaffFeedbackEnum? StaffFeedback { get; set; }

    /// <summary>User Staff đã đánh giá (GUID, từ token). <c>null</c> khi chưa có feedback.</summary>
    public string? StaffFeedbackByUserId { get; set; }

    /// <summary>Thời điểm đánh giá (UTC). <c>null</c> khi chưa có feedback.</summary>
    public DateTime? StaffFeedbackAt { get; set; }
}
