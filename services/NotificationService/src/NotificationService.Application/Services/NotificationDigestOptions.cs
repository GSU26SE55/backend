namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.2 NOTI-12 (#683) — cấu hình gom digest.
///
/// <c>NotificationPreference.Frequency</c> và <c>DigestWindowMinutes</c> đã tồn tại trong entity +
/// API PUT preferences từ trước, nhưng KHÔNG có logic nào đọc chúng (reviewnotification.md §4.5).
/// Sprint này chọn nhánh "implement" thay vì gỡ field.
/// </summary>
public class NotificationDigestOptions
{
    public const string SectionName = "Notification:Digest";

    public bool Enabled { get; set; } = true;

    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>Số user xử lý tối đa mỗi vòng.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Số dòng tối đa liệt kê trong thân digest (phần dư rút gọn thành "… và N thông báo khác").</summary>
    public int MaxItemsInBody { get; set; } = 10;
}

/// <summary>
/// Hằng nhận diện record digest tổng hợp.
///
/// Bản tổng hợp do <c>NotificationDigestBackgroundService</c> tạo ra cũng là một record Pending trên
/// kênh Email/Push của user vốn đang bật digest — nếu không đánh dấu thì
/// <c>NotificationDispatcher</c> lại hoãn nó vào digest lần nữa và không bao giờ gửi (vòng lặp vô hạn).
/// Dispatcher kiểm tra <see cref="EntityType"/> để bỏ qua bước gom digest cho đúng loại record này.
/// </summary>
public static class NotificationDigest
{
    public const string EntityType = "NotificationDigest";
}
