namespace NotificationService.Domain.Enums;

/// <summary>
/// Đường vận chuyển của kênh <see cref="NotificationChannelEnum.Push"/>.
///
/// <para>Đây là cấu hình HỆ THỐNG (một giá trị cho cả service), khác với
/// <c>NotificationPreference.PushEnabled</c> là tuỳ chọn của từng người dùng. Tắt kênh Push ở
/// preference thì không nhận push bằng đường nào; đổi transport ở đây chỉ đổi cách gói tin đi.</para>
///
/// <para>Đổi được lúc chạy qua <c>PUT /api/admin/notification-settings/push-transport</c> — không
/// phải sửa file cấu hình rồi khởi động lại service.</para>
/// </summary>
public enum PushTransportEnum
{
    /// <summary>
    /// Chỉ đẩy qua hub SignalR của chính hệ thống. Không phụ thuộc EAS/FCM, không cần device token.
    /// Máy nhận dựng thông báo hệ điều hành tại chỗ từ sự kiện <c>NotificationReceived</c>.
    /// </summary>
    SignalR = 1,

    /// <summary>
    /// Chỉ đẩy qua Expo Push API. Cần device token còn hoạt động; có đối soát biên nhận
    /// (<c>push_receipts</c>) nên mới lên được trạng thái <c>Delivered</c>.
    /// </summary>
    Expo = 2,

    /// <summary>
    /// Đẩy cả hai đường cho cùng một thông báo. Coi là gửi thành công khi ÍT NHẤT MỘT đường thành
    /// công — máy đang mở app nhận tức thì qua SignalR, máy đang tắt app vẫn nhận qua Expo.
    /// </summary>
    Both = 3,
}
