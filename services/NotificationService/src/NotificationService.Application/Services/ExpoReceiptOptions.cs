namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-02 (#702) — cấu hình đối soát biên nhận Expo
/// (section <c>Notification:ExpoReceipt</c>).
/// </summary>
public class ExpoReceiptOptions
{
    public const string SectionName = "Notification:ExpoReceipt";

    /// <summary>Bật/tắt worker. Tắt = mất khả năng biết push có tới nơi hay không.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Chu kỳ quét (giây). Tối thiểu 10s — worker tự kẹp sàn để không quay tít.</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Chỉ hỏi receipt của message đã gửi cách đây tối thiểu bấy nhiêu phút.
    /// Expo khuyến nghị chờ ~15': hỏi sớm hơn thì gần như luôn nhận về rỗng, tốn request vô ích.
    /// </summary>
    public int MinAgeMinutes { get; set; } = 15;

    /// <summary>Số ticket id hỏi trong 1 request. Expo cho tối đa 1000; worker tự kẹp trần.</summary>
    public int BatchSize { get; set; } = 300;

    /// <summary>
    /// Hỏi quá số lần này mà Expo vẫn không trả kết quả ⇒ đánh <c>Expired</c>.
    /// Expo chỉ giữ receipt ~24h nên hỏi mãi cũng không bao giờ có câu trả lời.
    /// </summary>
    public int MaxCheckAttempts { get; set; } = 5;
}
