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
    Opened = 6,

    /// <summary>
    /// GH-792 — đã CHIẾM để gửi, chưa biết kết quả.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trước đây bản ghi ở nguyên <see cref="Pending"/> trong suốt lúc gọi provider, và chỉ chuyển
    /// <see cref="Sent"/> SAU khi provider đã nhận. Tiến trình chết đúng khoảng giữa hai việc đó là
    /// bản ghi vẫn <see cref="Pending"/>: vòng quét sau gửi lại, người dùng nhận email/SMS/push lần
    /// thứ hai và không cách nào đối soát được đã gửi mấy lần.
    /// </para>
    /// <para>
    /// Trạng thái này được ghi và COMMIT <b>trước</b> khi gọi provider, nên sau sự cố bản ghi nằm ở
    /// đây chứ không rơi lại hàng đợi. Bản ghi kẹt quá lâu sẽ được thu hồi về <see cref="Pending"/>
    /// (xem <c>NotificationDispatchBackgroundService</c>) — và lần gửi lại đó được chặn trùng ở phía
    /// nhận nhờ ID message sinh theo <c>NotificationId</c>.
    /// </para>
    /// </remarks>
    Processing = 7
}
