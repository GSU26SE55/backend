namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-06 (#706) — hạn mức notification cho mỗi người dùng
/// (section <c>Notification:RateLimit</c>).
///
/// **Vấn đề:** một pin lỗi dao động quanh ngưỡng có thể sinh hàng chục cảnh báo trong một giờ.
/// Người nhận sẽ tắt thông báo — và tắt xong thì cảnh báo P1 thật sự cũng không tới được nữa.
/// Hạn mức là để giữ kênh còn đáng tin, không phải để tiết kiệm tài nguyên.
///
/// **Quan trọng:** vượt hạn mức thì **gom vào digest**, KHÔNG vứt bỏ. Vứt notification là mất dữ liệu
/// nghiệp vụ, còn hoãn thì người dùng vẫn thấy đầy đủ, chỉ gộp lại một lần.
/// </summary>
public class NotificationRateLimitOptions
{
    public const string SectionName = "Notification:RateLimit";

    /// <summary>Bật/tắt hạn mức.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Trần tổng số notification chủ động (Push/Email/SMS) mỗi người mỗi giờ.</summary>
    public int MaxPerUserPerHour { get; set; } = 20;

    /// <summary>
    /// Trần riêng cho từng <c>NotificationTypeEnum</c> mỗi người mỗi giờ.
    /// Chặn một loại sự kiện lặp lại chiếm hết hạn mức chung, che mất các loại khác.
    /// </summary>
    public int MaxPerUserPerType { get; set; } = 8;

    /// <summary>
    /// Thời gian hoãn khi chạm trần — notification sẽ nằm chờ rồi được gộp vào digest.
    /// </summary>
    public int DeferMinutes { get; set; } = 60;
}
