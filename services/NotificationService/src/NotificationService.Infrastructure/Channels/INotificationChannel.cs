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
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public bool IsCritical { get; set; }

    /// <summary>Expo push token — dùng cho ExpoPushChannel.</summary>
    public string? ExpoToken { get; set; }

    /// <summary>Địa chỉ email người nhận — dùng cho EmailBusChannel.</summary>
    public string? Email { get; set; }

    /// <summary>Số điện thoại người nhận — dùng cho SmsBusChannel.</summary>
    public string? PhoneNumber { get; set; }
}

public record ChannelResult(bool Success, string? ErrorMessage = null);
