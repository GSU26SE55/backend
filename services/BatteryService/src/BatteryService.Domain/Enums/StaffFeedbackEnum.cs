namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2, Q12=A) — feedback loop: Staff xác nhận classification của AI sau khi
/// resolve (spec §30.3). Phục vụ đánh giá precision/recall + xuất dữ liệu retrain (§30.12).
/// Giá trị: <c>1</c> Correct (AI đúng) · <c>2</c> FalsePositive (AI báo nhầm) · <c>3</c> FalseNegative (AI bỏ sót).
/// </summary>
public enum StaffFeedbackEnum
{
    /// <summary>AI phân loại đúng.</summary>
    Correct = 1,

    /// <summary>AI báo bất thường nhưng thực tế bình thường (false positive).</summary>
    FalsePositive = 2,

    /// <summary>AI bỏ sót bất thường thật (false negative).</summary>
    FalseNegative = 3
}
