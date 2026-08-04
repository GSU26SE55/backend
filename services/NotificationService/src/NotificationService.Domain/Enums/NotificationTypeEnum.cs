namespace NotificationService.Domain.Enums;

/// <summary>
/// Loại notification — phục vụ template routing và filter UI.
/// Bám theo §3.3 overall.md (NotificationService).
/// </summary>
public enum NotificationTypeEnum
{
    TicketCreated = 1,
    TicketAssigned = 2,
    TicketStatusChanged = 3,
    TicketResolved = 4,
    TicketClosed = 5,
    TicketEscalated = 6,
    SlaWarning = 7,
    SlaBreached = 8,
    BatteryAnomalyDetected = 9,
    EnvironmentalIncidentDetected = 10,
    EnvironmentalIncidentResolved = 11,
    AccountActivated = 12,
    // 13 = AdminInvite — ĐÃ GỠ 03/08/2026, cố ý để trống số này.
    //
    // Thư mời quản trị đi thẳng AuthService → EmailService (`SendAdminInviteEvent` →
    // `SendAdminInviteConsumer`), KHÔNG qua NotificationService. Ở đây nó là enum KHÔNG CÓ
    // PRODUCER: không consumer nào ghi, không dòng notifications nào mang type 13, nhưng vẫn
    // chiếm một ô trong ma trận kênh và một template trong DB — người vận hành sửa template đó
    // mãi mà chẳng đổi được gì.
    //
    // Đây đúng thứ mà ghi chú NOTI-04 phía dưới đã chốt tránh, chỉ là lúc đó bỏ sót chính dòng
    // này. Không dùng lại số 13 cho loại khác: dữ liệu cũ có thể còn tham chiếu.
    IncidentDeclared = 14,

    /// <summary>
    /// Sprint Bonus NS-14 (#658, R3) — cascade risk cao (≥ 0.7) trên 1 pin
    /// (<c>BatteryCascadeRiskHighEvent</c>) → notify Manager (push + email).
    /// </summary>
    CascadeRiskHigh = 15,

    /// <summary>
    /// Sprint 5B #238 — Critical Alert chưa-ack > 5 phút
    /// (<c>BatteryAlertEscalationRequestedEvent</c>).
    /// </summary>
    BatteryAlertEscalationPending = 16,

    /// <summary>
    /// Sprint 5B #238 — Alert–Ticket Saga vào terminal state Failed,
    /// admin reprocess required (<c>AlertTicketSagaFailedEvent</c>).
    /// </summary>
    AlertTicketSagaFailed = 17,

    /// <summary>Sprint IoT-1 (#249) — IoT edge device mất heartbeat &gt; 5 phút.</summary>
    IotDeviceWentOffline = 18,

    /// <summary>Sprint Chat Wave 4 (#544) — chat mới (public) trên ticket.</summary>
    ChatCreated = 19,

    /// <summary>Sprint Chat Wave 4 (#544) — user được mention trong chat.</summary>
    ChatMentioned = 20,

    /// <summary>Sprint Chat Wave 4 (#544) — chat của user nhận được reaction.</summary>
    ChatReacted = 21,

    /// <summary>Sprint Chat Wave 4 (#544) — user được thêm làm participant ticket (welcome).</summary>
    ParticipantAdded = 22,

    /// <summary>Sprint Chat Wave 4 (#544) — user bị xóa khỏi participant ticket (farewell).</summary>
    ParticipantRemoved = 23,

    /// <summary>Sprint Chat Wave 4 (#544) — role/type của participant trên ticket bị đổi.</summary>
    ParticipantRoleChanged = 24,

    /// <summary>GH-671 — AI blog generation hoàn thành thành công (status=Draft).</summary>
    BlogGenerationCompleted = 25,

    /// <summary>GH-671 — AI blog generation thất bại.</summary>
    BlogGenerationFailed = 26,

    /// <summary>
    /// Sprint 6.2 NOTI-03 (#674) — saga ChatEscalationReview timeout 30' (Manager không ACK)
    /// → escalate lên Admin (<c>ChatEscalatedToAdminEvent</c>).
    /// </summary>
    ChatEscalatedToAdmin = 27,

    /// <summary>Sprint 6.2 NOTI-07 (#678) — Manager duyệt kết quả xử lý, ticket chờ Customer đánh giá.</summary>
    TicketApproved = 28,

    /// <summary>Sprint 6.2 NOTI-07 (#678) — kết quả resolve bị từ chối, hoặc ticket bị đóng do ngoài scope.</summary>
    TicketRejected = 29,

    /// <summary>Sprint 6.2 NOTI-07 (#678) — Customer mở lại ticket.</summary>
    TicketReopened = 30,

    /// <summary>Sprint 6.2 NOTI-07 (#678) — nhắc Customer đánh giá ticket đang chờ.</summary>
    TicketRatingRequested = 31,

    /// <summary>Sprint 6.2 NOTI-08 (#679) — bất thường pin mức Warning (spec §3.4 T#12).</summary>
    BatteryAnomalyWarning = 32,

    /// <summary>Sprint 6.2 NOTI-08 (#679) — bất thường pin mức Info (spec §3.4 T#11, chỉ in-app).</summary>
    BatteryAnomalyInfo = 33,

    // Ghi chú Sprint 6.2 NOTI-04 (#675): cảnh báo bảo mật (suspicious login / refresh-token reuse)
    // đi thẳng đường AuthService → EmailService như OTP, KHÔNG qua NotificationService — nên cố ý
    // KHÔNG thêm type ở đây để tránh đẻ thêm enum không producer (đúng lỗi mà review §4.8 đã nêu).
    /// <summary>
    /// GH-699.1 — source ticket đã được gộp vào master ticket.
    ///
    /// GH-83 (2026-08-01): đổi <c>27 → 34</c>. Giá trị 27 đã thuộc về
    /// <see cref="ChatEscalatedToAdmin"/>; hai tên trùng giá trị là hợp lệ với compiler nhưng khiến
    /// <c>NotificationCategoryMap</c> (dictionary khoá theo giá trị enum) **không thể** chứa
    /// <c>TicketMerged</c> như một khoá riêng → nó bị xếp nhầm nhóm <c>Sla</c> và biến mất khỏi
    /// <c>GET /api/notification-preferences/categories</c>. Trùng số cũng khiến hai loại tranh
    /// cùng khoá template <c>(type, channel)</c>. Mobile đã chốt 34 và không dùng lại 27.
    /// An toàn: bảng <c>notifications</c> không có record nào <c>type = 27</c> khi đổi.
    /// </summary>
    TicketMerged = 34,

    System = 99
}
