using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

public interface INotificationDispatcher
{
    Task DispatchAsync(DispatchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gửi 1 record <see cref="Notification"/> đang ở trạng thái Pending (GH-672 NOTI-01).
    /// Khác <see cref="DispatchAsync"/> ở chỗ KHÔNG tạo record mới — chỉ gửi record có sẵn
    /// rồi chốt Status thành Sent/Failed.
    /// </summary>
    /// <returns>
    /// <c>true</c> nếu đã chốt trạng thái (Sent hoặc Failed);
    /// <c>false</c> nếu hoãn — record giữ nguyên Pending để tick sau xử lý lại
    /// (quiet hours, hoặc channel Email chờ #673).
    /// </returns>
    Task<bool> DispatchPendingAsync(Notification notification, CancellationToken ct = default);
}

public class RecipientInfo
{
    public Guid UserId { get; set; }

    /// <summary>Null → email channel bị skip.</summary>
    public string? Email { get; set; }

    /// <summary>Null → SMS channel bị skip.</summary>
    public string? PhoneNumber { get; set; }
}
