using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Cấu hình tĩnh của kênh Push, đọc từ appsettings lúc khởi động.
///
/// <para><b>Quan hệ với bảng <c>notification_settings</c>:</b> <see cref="DefaultTransport"/> chỉ
/// dùng cho lần chạy đầu khi bảng còn chưa có dòng nào. Sau khi Admin đổi transport qua REST thì
/// giá trị trong DB là nguồn sự thật và appsettings không còn ảnh hưởng nữa — nếu không, mỗi lần
/// khởi động lại service sẽ ghi đè lựa chọn của người vận hành.</para>
/// </summary>
public class NotificationPushOptions
{
    public const string SectionName = "Notification:Push";

    /// <summary>
    /// Đường vận chuyển mặc định khi bảng cấu hình còn trống. Mặc định
    /// <see cref="PushTransportEnum.SignalR"/> vì nó không cần khoá EAS/FCM nào để chạy được.
    /// </summary>
    public PushTransportEnum DefaultTransport { get; set; } = PushTransportEnum.SignalR;

    /// <summary>
    /// Thời gian nhớ giá trị transport trong cache. Ngắn để một lần đổi trên màn hình Admin có hiệu
    /// lực gần như tức thì kể cả ở tiến trình không xử lý request đổi đó (ví dụ worker nền, hoặc
    /// replica khác). Bản thân request đổi luôn xoá cache ngay nên không phải chờ hết hạn.
    /// </summary>
    public int CacheSeconds { get; set; } = 30;
}
