namespace NotificationService.Domain.Enums;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — ánh xạ <see cref="NotificationTypeEnum"/> → <see cref="NotificationCategoryEnum"/>.
///
/// Đặt ở Domain (không phải config) vì đây là phân loại nghiệp vụ cố định, không phải tham số vận hành:
/// người vận hành đổi được *ai nhận gì*, nhưng không đổi được *"SLA sắp vỡ" thuộc nhóm nào*.
///
/// Có test bao mọi giá trị enum — thêm type mới mà quên khai báo ở đây thì test đỏ ngay, thay vì
/// âm thầm rơi vào nhóm mặc định và né mất tuỳ chọn của người dùng.
/// </summary>
public static class NotificationCategoryMap
{
    private static readonly IReadOnlyDictionary<NotificationTypeEnum, NotificationCategoryEnum> Map =
        new Dictionary<NotificationTypeEnum, NotificationCategoryEnum>
        {
            // ── Ticket ──────────────────────────────────────────────────────
            [NotificationTypeEnum.TicketCreated] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketAssigned] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketStatusChanged] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketResolved] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketClosed] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketApproved] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketRejected] = NotificationCategoryEnum.Ticket,
            // 03/08/2026 — trước nay KHÔNG có dòng này, và test bao vẫn xanh: TicketMerged mang
            // giá trị 27 trùng ChatEscalatedToAdmin nên vô tình ăn theo nhóm Sla của loại kia. Tức
            // là thông báo "ticket đã gộp" bị xếp nhầm nhóm SLA, kéo theo tuỳ chọn nhận thông báo
            // theo nhóm cũng áp sai. Đổi enum sang 34 làm lộ ra chỗ này.
            [NotificationTypeEnum.TicketMerged] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketReopened] = NotificationCategoryEnum.Ticket,
            [NotificationTypeEnum.TicketRatingRequested] = NotificationCategoryEnum.Ticket,
            // GH-83: trước đây không khai báo được vì TicketMerged trùng giá trị 27 với
            // ChatEscalatedToAdmin ⇒ cùng một khoá dictionary. Sau khi đổi sang 34 mới thêm được.
            [NotificationTypeEnum.TicketMerged] = NotificationCategoryEnum.Ticket,

            // ── SLA & leo thang ─────────────────────────────────────────────
            // TicketEscalated nằm ở nhóm SLA (không phải Ticket): leo thang luôn là hệ quả của
            // rủi ro vỡ cam kết, và người dùng tắt "cập nhật ticket" vẫn phải nhận được nó.
            [NotificationTypeEnum.TicketEscalated] = NotificationCategoryEnum.Sla,
            [NotificationTypeEnum.SlaWarning] = NotificationCategoryEnum.Sla,
            [NotificationTypeEnum.SlaBreached] = NotificationCategoryEnum.Sla,
            [NotificationTypeEnum.IncidentDeclared] = NotificationCategoryEnum.Sla,
            [NotificationTypeEnum.AlertTicketSagaFailed] = NotificationCategoryEnum.Sla,
            [NotificationTypeEnum.ChatEscalatedToAdmin] = NotificationCategoryEnum.Sla,

            // ── Pin & thiết bị ──────────────────────────────────────────────
            [NotificationTypeEnum.BatteryAnomalyDetected] = NotificationCategoryEnum.Battery,
            [NotificationTypeEnum.BatteryAnomalyWarning] = NotificationCategoryEnum.Battery,
            [NotificationTypeEnum.BatteryAnomalyInfo] = NotificationCategoryEnum.Battery,
            [NotificationTypeEnum.BatteryAlertEscalationPending] = NotificationCategoryEnum.Battery,
            [NotificationTypeEnum.CascadeRiskHigh] = NotificationCategoryEnum.Battery,
            [NotificationTypeEnum.IotDeviceWentOffline] = NotificationCategoryEnum.Battery,

            // ── Sự cố môi trường ────────────────────────────────────────────
            [NotificationTypeEnum.EnvironmentalIncidentDetected] = NotificationCategoryEnum.Environmental,
            [NotificationTypeEnum.EnvironmentalIncidentResolved] = NotificationCategoryEnum.Environmental,

            // ── Trao đổi ────────────────────────────────────────────────────
            [NotificationTypeEnum.ChatCreated] = NotificationCategoryEnum.Chat,
            [NotificationTypeEnum.ChatMentioned] = NotificationCategoryEnum.Chat,
            [NotificationTypeEnum.ChatReacted] = NotificationCategoryEnum.Chat,
            [NotificationTypeEnum.ParticipantAdded] = NotificationCategoryEnum.Chat,
            [NotificationTypeEnum.ParticipantRemoved] = NotificationCategoryEnum.Chat,
            [NotificationTypeEnum.ParticipantRoleChanged] = NotificationCategoryEnum.Chat,

            // ── Tài khoản & hệ thống ────────────────────────────────────────
            [NotificationTypeEnum.AccountActivated] = NotificationCategoryEnum.Account,
            [NotificationTypeEnum.System] = NotificationCategoryEnum.Account,
            // GH-83: hai type Blog trước đây thiếu khai báo ⇒ rơi vào nhánh mặc định của Resolve()
            // (cũng là Account) nên hành vi runtime KHÔNG đổi — nhưng test bao vẫn đỏ vì `All` thiếu
            // khoá, và FE không thấy chúng trong GET /categories.
            [NotificationTypeEnum.BlogGenerationCompleted] = NotificationCategoryEnum.Account,
            [NotificationTypeEnum.BlogGenerationFailed] = NotificationCategoryEnum.Account,
        };

    /// <summary>
    /// Nhóm của một type. Type chưa khai báo ⇒ <see cref="NotificationCategoryEnum.Account"/>
    /// (nhóm "hệ thống"): thà rơi vào nhóm chung còn hơn ném lỗi làm chết cả đường gửi.
    /// Test bao sẽ bắt trường hợp thiếu khai báo ngay ở CI.
    /// </summary>
    public static NotificationCategoryEnum Resolve(NotificationTypeEnum type) =>
        Map.TryGetValue(type, out var category) ? category : NotificationCategoryEnum.Account;

    /// <summary>Toàn bộ ánh xạ — phục vụ test bao và endpoint trả bảng nhóm cho FE.</summary>
    public static IReadOnlyDictionary<NotificationTypeEnum, NotificationCategoryEnum> All => Map;
}
