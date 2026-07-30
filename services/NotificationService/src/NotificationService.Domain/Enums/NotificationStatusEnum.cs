namespace NotificationService.Domain.Enums;

/// <summary>
/// Trạng thái lifecycle của notification record. §3.3 overall.md.
/// </summary>
public enum NotificationStatusEnum
{
    /// <summary>Đã tạo bản ghi, chưa gửi xuống channel.</summary>
    Pending = 1,

    /// <summary>Đã bàn giao cho channel (Expo/Mailjet/SMS gateway) — CHƯA chắc thiết bị nhận được.</summary>
    Sent = 2,

    /// <summary>Gửi thất bại (hết số lần thử hoặc channel trả lỗi vĩnh viễn).</summary>
    Failed = 3,

    /// <summary>User đã mark read trên feed in-app.</summary>
    Read = 4,

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — provider **xác nhận** đã đẩy tới thiết bị.
    /// Với push: Expo receipt trả <c>status:"ok"</c> (NOTI3-02). Đây mới là bằng chứng giao hàng thật;
    /// <see cref="Sent"/> chỉ chứng minh Expo đã nhận request.
    /// </summary>
    Delivered = 5,

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — user đã mở notification (bấm vào push / deep link).
    /// Mạnh hơn <see cref="Read"/>: Read có thể chỉ do lướt qua feed.
    /// </summary>
    Opened = 6
}
