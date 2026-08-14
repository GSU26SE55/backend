namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-05 (#705) — cấu hình chuỗi dự phòng push → SMS
/// (section <c>Notification:Fallback</c>).
///
/// **Nhánh B (chốt 30/07/2026):** chỉ fallback nội bộ giữa các kênh sẵn có, KHÔNG mua provider thứ hai.
/// Giới hạn phải chấp nhận (R-44): chuỗi này cứu được ca *push hỏng*, KHÔNG cứu được ca *SMS hỏng* —
/// gateway SMS là một chiếc điện thoại Android duy nhất.
/// </summary>
public class NotificationFallbackOptions
{
    public const string SectionName = "Notification:Fallback";

    /// <summary>Bật/tắt toàn bộ chuỗi dự phòng.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Push gửi đi quá bấy nhiêu phút mà vẫn chưa có receipt <c>ok</c> ⇒ coi như không tới nơi và
    /// bù bằng SMS.
    ///
    /// <para>
    /// ⚠️ <b>Giá trị này KHÔNG độc lập.</b> Nó phải lớn hơn thời điểm sớm nhất mà worker đối soát
    /// (NOTI3-02) có thể biết kết quả, nếu không fallback sẽ bắn SMS trong khi receipt còn chưa
    /// được phép hỏi — tức là gửi thừa cho **mọi** push critical.
    /// </para>
    /// <code>
    /// ngưỡng an toàn tối thiểu = ExpoReceipt:MinAgeMinutes          (15')  ← sớm nhất được hỏi Expo
    ///                          + ExpoReceipt:PollIntervalSeconds/60  (5')  ← chu kỳ quét, xấu nhất lỡ 1 nhịp
    ///                          + biên dự phòng cho độ trễ HTTP      (~5')
    ///                          = 25 phút
    /// </code>
    /// <para>
    /// Mặc định <b>30</b> — trên ngưỡng đó một chút. <c>NotificationFallbackBackgroundService</c>
    /// tự kiểm tra lúc khởi động và ghi cảnh báo nếu cấu hình vi phạm ràng buộc này.
    /// </para>
    /// </summary>
    public int PushReceiptTimeoutMinutes { get; set; } = 30;

    /// <summary>Chu kỳ quét (giây).</summary>
    public int PollIntervalSeconds { get; set; } = 120;

    /// <summary>Số notification xử lý mỗi vòng — chặn một đợt sự cố làm nghẽn worker.</summary>
    public int BatchSize { get; set; } = 100;
}
