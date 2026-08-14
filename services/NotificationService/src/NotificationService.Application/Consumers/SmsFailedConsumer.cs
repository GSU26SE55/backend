using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-11 (#682) — vòng phản hồi cho SMS.
///
/// <c>SmsBusChannel</c> publish <c>SendSmsCommand</c> với <c>CorrelationId = Notification.Id</c> rồi
/// đánh dấu record là Sent ngay khi đẩy được lên bus. Nếu SmsService gửi thất bại thật (đã hết retry)
/// nó publish <see cref="SmsFailedEvent"/> nhưng KHÔNG ai consume → record notification kẹt ở
/// <c>Sent</c> dù SMS không bao giờ tới (reviewnotification.md §3.3).
///
/// Consumer này đối chiếu <c>CorrelationId</c> về đúng record và hạ trạng thái xuống
/// <see cref="NotificationStatusEnum.Failed"/> kèm <c>FailureReason</c>.
/// </summary>
public class SmsFailedConsumer : IConsumer<SmsFailedEvent>
{
    private const string NotificationSource = "notification";

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<SmsFailedConsumer> _logger;

    public SmsFailedConsumer(INotificationUnitOfWork unitOfWork, ILogger<SmsFailedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SmsFailedEvent> context)
    {
        var evt = context.Message;

        // SMS của service khác (auth OTP, battery alert…) không có record notification tương ứng.
        if (!string.Equals(evt.SourceService, NotificationSource, StringComparison.OrdinalIgnoreCase))
            return;

        if (evt.CorrelationId == Guid.Empty)
            return;

        var notification = await _unitOfWork.Notifications.GetAllAsync()
            .FirstOrDefaultAsync(
                n => n.Id == evt.CorrelationId
                     && !n.IsDeleted
                     && n.Channel == NotificationChannelEnum.Sms,
                context.CancellationToken);

        if (notification is null)
        {
            _logger.LogInformation(
                "SmsFailed: không tìm thấy notification SMS {CorrelationId} (có thể đã bị dọn) — bỏ qua.",
                evt.CorrelationId);
            return;
        }

        // Đã đọc / đã mở rồi thì không hạ trạng thái nữa — tránh ghi đè hành vi người dùng.
        // Sprint 6.3 NOTI3-14 (#714) — Opened cũng là bằng chứng người dùng đã tiếp nhận.
        if (notification.Status is NotificationStatusEnum.Read or NotificationStatusEnum.Opened)
            return;

        notification.Status = NotificationStatusEnum.Failed;
        notification.SentAt = null;
        notification.FailureReason = Truncate(
            $"SMS gateway failed ({evt.PhoneNumber}): {evt.ErrorMessage ?? "unknown reason"}", 1000);
        notification.NextAttemptAt = null;

        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(context.CancellationToken);

        _logger.LogWarning(
            "SmsFailed: notification {NotificationId} chuyển sang Failed (smsId={SmsId}).",
            notification.Id, evt.SmsId);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
