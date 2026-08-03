using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// InApp channel — notification đã có trong DB (Pending).
/// SendAsync update Status=Sent để FE polling endpoint thấy được.
///
/// Sprint 6.3 NOTI3-13 (#713) — kèm đẩy realtime qua SignalR để client đang mở app thấy ngay,
/// không phải chờ vòng polling kế tiếp. Polling giữ nguyên làm đường dự phòng khi WebSocket rớt.
/// </summary>
public class InAppChannel : INotificationChannel
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationRealtimeNotifier? _realtime;   // Sprint 6.3 NOTI3-13 (#713)
    private readonly ILogger<InAppChannel> _logger;

    public InAppChannel(
        INotificationUnitOfWork unitOfWork,
        ILogger<InAppChannel> logger,
        // Optional để test/caller cũ không phải sửa; thiếu = chỉ mất realtime, feed vẫn đúng.
        INotificationRealtimeNotifier? realtime = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _realtime = realtime;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.InApp;

    /// <summary>Khớp <c>NotificationConfiguration</c>: cột <c>title</c> 200, <c>body</c> 2000.</summary>
    private const int TitleMaxLength = 200;
    private const int BodyMaxLength = 2000;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(request.NotificationId);
        if (notification is null || notification.IsDeleted)
        {
            _logger.LogWarning("InAppChannel: notification {NotificationId} not found", request.NotificationId);
            return new ChannelResult(false, "Notification not found");
        }

        // Idempotent — không update lại nếu đã Sent
        if (notification.Status == NotificationStatusEnum.Sent)
            return new ChannelResult(true);

        // 03/08/2026 — ghi NGƯỢC nội dung đã render vào chính dòng notification.
        //
        // Vì sao cần: với ba kênh kia, thứ người dùng nhận là gói tin gửi đi, còn dòng trong DB chỉ
        // là biên bản. Với InApp thì ngược lại — dòng trong DB CHÍNH LÀ thứ người dùng đọc, vì
        // feed và realtime đều lấy từ đó. Trước thay đổi này dispatcher vẫn tra template và render
        // cho InApp, rồi vứt kết quả đi: 548/1285 dòng (43%) tốn một truy vấn và một lượt render mà
        // không ai thấy, còn 33 template InApp thì sửa được, xem trước được, nhưng sửa xong không
        // đổi được chữ nào trên màn hình.
        //
        // Vì sao ghi vào dòng này là đúng chỗ chứ không phải chắp vá: bảng notifications đã có cột
        // channel, mỗi dòng đúng một kênh. Nên nội dung-render-cho-kênh-đó thuộc về chính dòng đó.
        //
        // Vì sao an toàn: dispatcher chỉ nhặt dòng Pending, cộng với chốt idempotent ngay trên, nên
        // mỗi dòng chỉ ghi đúng một lần. Bản tin gom (digest) không đi đường này — nó tự đánh Sent
        // cho các mục con nên chúng giữ nguyên nội dung gốc.
        //
        // Vì sao phải cắt: title_template tối đa 500 và body_template tối đa 4000, trong khi cột
        // title chỉ 200 và body chỉ 2000. Một template dài hơn cột sẽ làm Postgres ném lỗi và dòng
        // đó kẹt lại retry vĩnh viễn. Cắt bớt vẫn hơn là không gửi được.
        if (!string.IsNullOrWhiteSpace(request.Title))
            notification.Title = Truncate(request.Title, TitleMaxLength);

        if (!string.IsNullOrWhiteSpace(request.Body))
            notification.Body = Truncate(request.Body, BodyMaxLength);

        notification.Status = NotificationStatusEnum.Sent;
        notification.SentAt = DateTime.UtcNow;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        // Sprint 6.3 NOTI3-13 (#713) — đẩy realtime SAU khi đã lưu, để client không bao giờ thấy
        // một thông báo mà REST chưa trả về. Notifier tự nuốt lỗi: mất realtime không được làm
        // hỏng bản ghi thật (client vẫn nhận qua polling).
        if (_realtime is not null)
        {
            await _realtime.NotifyCreatedAsync(notification, ct);

            var unread = await _unitOfWork.Notifications.GetAllAsync(false)
                .CountAsync(n => n.UserId == notification.UserId
                                 && !n.IsDeleted
                                 && n.Channel == NotificationChannelEnum.InApp
                                 && n.Status != NotificationStatusEnum.Read
                                 && n.Status != NotificationStatusEnum.Opened, ct);

            await _realtime.NotifyUnreadCountAsync(notification.UserId, unread, ct);
        }

        return new ChannelResult(true);
    }
}
