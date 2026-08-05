using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

public interface INotificationChannel
{
    NotificationChannelEnum ChannelType { get; }
    Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default);
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
    public bool IsCritical { get; set; }

    /// <summary>Entity linked to the notification; used by clients for deep links.</summary>
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>Original creation time, used by clients for ordering and deduplication.</summary>
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
