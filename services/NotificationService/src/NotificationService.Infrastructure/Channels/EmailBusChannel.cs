using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Email channel — publish <see cref="SendNotificationEmailEvent"/> lên RabbitMQ.
/// EmailService consume và gửi email thực tế — template rendering xử lý ở #111.
///
/// Sprint 6.3 NOTI3-15 (#715) — kèm URL hủy đăng ký một chạm. Mọi email đi qua kênh này đều là
/// email KHÔNG giao dịch (thông báo nghiệp vụ), nên đều phải có nút hủy; email giao dịch (OTP,
/// đặt lại mật khẩu) đi đường AuthService → EmailService riêng và cố ý không có.
/// </summary>
public class EmailBusChannel : INotificationChannel
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly UnsubscribeTokenService? _unsubscribeTokens;
    private readonly ILogger<EmailBusChannel> _logger;

    public EmailBusChannel(
        IPublishEndpoint publishEndpoint,
        ILogger<EmailBusChannel> logger,
        // Optional để test/caller cũ không phải sửa; thiếu = không gắn link hủy.
        UnsubscribeTokenService? unsubscribeTokens = null)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _unsubscribeTokens = unsubscribeTokens;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Email;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            _logger.LogWarning("EmailBusChannel: no email for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, "No email address");
        }

        try
        {
            await _publishEndpoint.Publish(
                new SendNotificationEmailEvent(
                    NotificationId: request.NotificationId,
                    ToEmail: request.Email,
                    Subject: request.Title,
                    Body: request.Body,
                    SourceService: "notification",
                    UnsubscribeUrl: BuildUnsubscribeUrl(request)
                ),
                ct);

            return new ChannelResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailBusChannel: failed to publish email for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-15 (#715) — dựng URL hủy đăng ký cho đúng NHÓM của notification, không phải
    /// cho toàn bộ email. Người dùng bấm hủy vì bị chat làm phiền không nên mất luôn cảnh báo SLA.
    ///
    /// Trả <c>null</c> khi chưa cấu hình khoá ký hoặc địa chỉ gốc — khi đó email vẫn gửi bình thường,
    /// chỉ không có nút hủy (mất điểm với Gmail/Yahoo nhưng không làm hỏng đường gửi).
    /// </summary>
    private string? BuildUnsubscribeUrl(SendRequest request)
    {
        if (_unsubscribeTokens is null)
            return null;

        var baseUrl = _unsubscribeTokens.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogDebug(
                "EmailBusChannel: chưa cấu hình Notification:Unsubscribe:PublicBaseUrl — gửi email không kèm link hủy.");
            return null;
        }

        var category = NotificationCategoryMap.Resolve(request.Type);
        var token = _unsubscribeTokens.Create(request.UserId, category);

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "EmailBusChannel: chưa cấu hình Notification:Unsubscribe:Secret — email gửi KHÔNG có "
                + "List-Unsubscribe, rủi ro bị báo cáo spam (NOTI3-15).");
            return null;
        }

        return $"{baseUrl.TrimEnd('/')}/api/notification-unsubscribe?token={Uri.EscapeDataString(token)}";
    }
}
