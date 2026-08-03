using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Domain.Entities;

namespace NotificationService.Application.Services;

public interface INotificationDispatcher
{
    /// <summary>
    /// Fan-out từ 1 sự kiện: tự chọn channel theo <c>TypeChannelMatrix</c>, TỰ TẠO record rồi gửi.
    /// Dùng cho caller đã có sẵn danh sách recipient và muốn dispatcher quyết định channel.
    /// </summary>
    Task DispatchAsync(DispatchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sprint 6.2 NOTI-01 (#672) — gửi MỘT record <c>Pending</c> ĐÃ TỒN TẠI xuống đúng channel
    /// của chính nó, rồi cập nhật <c>Status</c>/<c>SentAt</c>/<c>FailureReason</c>.
    ///
    /// Vì sao cần method riêng thay vì tái dùng <see cref="DispatchAsync"/>: DispatchAsync TỰ TẠO
    /// record mới, nên nếu worker quét Pending rồi gọi nó thì mỗi vòng quét sẽ nhân đôi dữ liệu.
    /// 22 consumer hiện tại đã tự ghi record (1 record / channel) — worker chỉ việc giao chúng đi.
    /// </summary>
    Task<DispatchOutcome> DispatchPendingAsync(Notification notification, CancellationToken ct = default);
}

/// <summary>Kết quả xử lý 1 record Pending.</summary>
public enum DispatchOutcome
{
    /// <summary>Đã gửi thành công xuống channel (Status = Sent).</summary>
    Sent = 1,

    /// <summary>Lỗi tạm thời — đã tăng attempt + hẹn <c>NextAttemptAt</c>, vẫn ở Pending.</summary>
    Retrying = 2,

    /// <summary>Lỗi vĩnh viễn hoặc hết lượt thử (Status = Failed).</summary>
    Failed = 3,

    /// <summary>Hoãn có chủ đích (quiet hours / digest) — vẫn Pending, KHÔNG tính là 1 lần thử.</summary>
    Deferred = 4,
}

public class RecipientInfo
{
    public Guid UserId { get; set; }

    /// <summary>Null → email channel bị skip.</summary>
    public string? Email { get; set; }

    /// <summary>Null → SMS channel bị skip.</summary>
    public string? PhoneNumber { get; set; }
}
