using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint Bonus NS-26 (#666) — body cho <c>POST /api/v1/anomaly-classifications/{id}/feedback</c>.
/// Chỉ nhận <see cref="Feedback"/>; user id lấy từ token (không nhận từ body — chống mạo danh).
/// </summary>
public class AnomalyClassificationFeedbackRequest
{
    /// <summary>
    /// Đánh giá của Staff (<c>StaffFeedbackEnum</c>, bắt buộc): <c>1</c> Correct (AI đúng) ·
    /// <c>2</c> FalsePositive (AI báo bất thường nhưng thực tế bình thường) ·
    /// <c>3</c> FalseNegative (AI bỏ sót bất thường thật).
    /// </summary>
    public StaffFeedbackEnum Feedback { get; set; }
}
