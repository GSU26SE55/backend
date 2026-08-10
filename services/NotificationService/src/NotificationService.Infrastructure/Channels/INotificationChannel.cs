using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

public interface INotificationChannel
{
    NotificationChannelEnum ChannelType { get; }
    Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default);
}

/// <summary>
/// ADR-0019 — đường push qua hub SignalR tự vận hành.
///
/// <para>Hai đường push cần interface riêng vì cả hai đều có <c>ChannelType = Push</c>: đăng ký
/// chúng dưới <see cref="INotificationChannel"/> thì dispatcher chỉ thấy cái đầu tiên và cái còn
/// lại chết im lặng. Chỉ <c>CompositePushChannel</c> mới đăng ký dưới interface chung.</para>
/// </summary>
public interface ISignalRPushChannel : INotificationChannel
{
}

/// <summary>ADR-0019 — đường push qua Expo Push API. Xem ghi chú ở <see cref="ISignalRPushChannel"/>.</summary>
public interface IExpoPushChannel : INotificationChannel
{
}

/// <summary>
/// Dữ liệu đủ để gửi qua bất kỳ channel nào.
/// Dispatcher populate các field channel-specific trước khi gọi SendAsync.
/// </summary>
public class SendRequest
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Sprint 6.3 NOTI3-15 (#715) — loại notification. Cần để dựng link hủy đăng ký theo đúng NHÓM
    /// (người dùng hủy vì chat làm phiền không nên mất luôn cảnh báo SLA).
    /// </summary>
    public NotificationTypeEnum Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }

    /// <summary>
    /// 03/08/2026 — cặp (loại thực thể, id) mà thông báo này trỏ tới, chép thẳng từ bản ghi
    /// notification.
    ///
    /// <para><b>Vì sao cần đưa xuống tận kênh gửi:</b> push chỉ mang <c>notificationId</c> cộng các
    /// khoá payload, nên client phải <i>đoán</i> mở màn nào — mà đoán theo khoá payload thì lệch
    /// ngay với danh sách trong ứng dụng (vốn dùng <c>entityType</c>). Cùng một thông báo, bấm ở
    /// feed ra một màn, bấm ở push ra màn khác. Gửi kèm cặp này để hai đường dùng chung một nguồn.</para>
    /// </summary>
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }
    public bool IsCritical { get; set; }

    /// <summary>
    /// ADR-0019 — thời điểm tạo bản ghi gốc (UTC). Client dùng để sắp xếp và khử trùng giữa hai
    /// đường push: cùng một thông báo có thể tới qua SignalR và qua Expo ở chế độ <c>Both</c>.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Expo push token đơn — giữ cho caller cũ. Khi <see cref="ExpoTokens"/> có giá trị thì
    /// <c>ExpoPushChannel</c> ưu tiên dùng danh sách đó.
    /// </summary>
    public string? ExpoToken { get; set; }

    /// <summary>
    /// Sprint 6.2 NOTI-16 (#687) — TẤT CẢ device token đang hoạt động của người nhận.
    /// <c>ExpoPushChannel</c> gộp tối đa 100 message / HTTP call (giới hạn của Expo Push API)
    /// thay vì mỗi token một request.
    /// </summary>
    public IReadOnlyList<string>? ExpoTokens { get; set; }

    /// <summary>Địa chỉ email người nhận — dùng cho EmailBusChannel.</summary>
    public string? Email { get; set; }

    /// <summary>Số điện thoại người nhận — dùng cho SmsBusChannel.</summary>
    public string? PhoneNumber { get; set; }
}

public record ChannelResult(bool Success, string? ErrorMessage = null);
