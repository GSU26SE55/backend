using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Sprint 6.3 NOTI3-12 (#712) — bộ template chuẩn, phủ **đủ 32 type × mọi kênh** của
/// <c>NotificationDispatchOptions.DefaultTypeChannelMatrix</c>.
///
/// **Vấn đề trước sprint này:** chỉ 5/32 type có template trong DB. Với các type còn lại, dispatcher
/// rơi về Title/Body mà consumer ghi cứng trong code — nghĩa là muốn sửa một câu chữ phải sửa code,
/// build lại, deploy lại. Có template trong DB thì người vận hành sửa được ngay.
///
/// **Locale:** tiếng Việt là mặc định; các type hướng **Customer** có thêm <c>en-US</c> vì khách hàng
/// có thể không đọc tiếng Việt. Type nội bộ (dành cho Staff/Manager/Admin) chỉ cần tiếng Việt —
/// dịch thứ không ai đọc chỉ tạo thêm thứ phải bảo trì.
///
/// Tách khỏi <c>NotificationDataSeeder</c> để test bao (mọi ô của ma trận có template chưa) đọc được
/// danh mục mà không phải dựng DbContext.
/// </summary>
public static class NotificationTemplateCatalog
{
    public const string DefaultLocale = "vi-VN";
    public const string EnglishLocale = "en-US";

    /// <summary>Một dòng của danh mục.</summary>
    public readonly record struct Entry(
        NotificationTypeEnum Type,
        NotificationChannelEnum Channel,
        string Locale,
        string Title,
        string Body);

    /// <summary>
    /// Các type hướng Customer — cần bản <c>en-US</c>.
    /// Type nội bộ (SLA, saga, IoT, chat…) chỉ phục vụ nhân sự vận hành nói tiếng Việt.
    /// </summary>
    public static readonly IReadOnlySet<NotificationTypeEnum> CustomerFacingTypes =
        new HashSet<NotificationTypeEnum>
        {
            NotificationTypeEnum.TicketCreated,
            NotificationTypeEnum.TicketStatusChanged,
            NotificationTypeEnum.TicketResolved,
            NotificationTypeEnum.TicketClosed,
            NotificationTypeEnum.TicketApproved,
            NotificationTypeEnum.TicketRejected,
            NotificationTypeEnum.TicketReopened,
            NotificationTypeEnum.TicketRatingRequested,
            NotificationTypeEnum.AccountActivated,
            NotificationTypeEnum.BatteryAnomalyWarning,
            NotificationTypeEnum.BatteryAnomalyInfo,
            NotificationTypeEnum.EnvironmentalIncidentDetected,
            NotificationTypeEnum.EnvironmentalIncidentResolved,
        };

    /// <summary>Nội dung tiếng Việt cho từng type — dùng chung cho mọi kênh, biến tấu theo kênh bên dưới.</summary>
    private static readonly IReadOnlyDictionary<NotificationTypeEnum, (string Title, string Body)> Vietnamese =
        new Dictionary<NotificationTypeEnum, (string, string)>
        {
            [NotificationTypeEnum.TicketCreated] =
                ("Ticket mới {{ticketCode}}", "Khách hàng {{customerName}} vừa tạo ticket {{ticketCode}}: {{title}}"),
            [NotificationTypeEnum.TicketAssigned] =
                ("Bạn được giao ticket {{ticketCode}}", "{{title}} — Mức ưu tiên {{priority}}. Hạn xử lý {{slaDeadline}}."),
            [NotificationTypeEnum.TicketStatusChanged] =
                ("Ticket {{ticketCode}} đổi trạng thái", "Từ {{oldStatus}} sang {{newStatus}}."),
            [NotificationTypeEnum.TicketResolved] =
                ("Ticket {{ticketCode}} đã xử lý xong", "Ticket {{ticketCode}} đã được xử lý. Vui lòng xác nhận và đánh giá."),
            [NotificationTypeEnum.TicketClosed] =
                ("Ticket {{ticketCode}} đã đóng", "Cảm ơn bạn đã sử dụng dịch vụ."),
            [NotificationTypeEnum.TicketEscalated] =
                ("Ticket {{ticketCode}} đã được leo thang", "Lý do: {{reason}}. Người phụ trách mới: {{assignedStaffName}}."),
            [NotificationTypeEnum.TicketApproved] =
                ("Kết quả xử lý ticket {{ticketCode}} đã được duyệt", "Vui lòng đánh giá chất lượng dịch vụ để đóng ticket."),
            [NotificationTypeEnum.TicketRejected] =
                ("Ticket {{ticketCode}} bị từ chối", "Lý do: {{reason}}."),
            [NotificationTypeEnum.TicketReopened] =
                ("Ticket {{ticketCode}} được mở lại", "Khách hàng chưa hài lòng với kết quả. Lý do: {{reason}}."),
            [NotificationTypeEnum.TicketRatingRequested] =
                ("Mời đánh giá ticket {{ticketCode}}", "Ticket đã hoàn tất. Đánh giá của bạn giúp chúng tôi phục vụ tốt hơn."),

            [NotificationTypeEnum.SlaWarning] =
                ("Cảnh báo SLA: {{ticketCode}}", "Còn {{minutesRemaining}} phút trước khi vỡ SLA {{priority}}."),
            [NotificationTypeEnum.SlaBreached] =
                ("VỠ SLA: {{ticketCode}}", "Ticket đã quá hạn SLA {{priority}}. Cần leo thang ngay."),
            [NotificationTypeEnum.IncidentDeclared] =
                ("Công bố sự cố nghiêm trọng", "{{incidentType}} — công bố bởi {{declaredBy}} lúc {{declaredAt}}."),
            [NotificationTypeEnum.AlertTicketSagaFailed] =
                ("Saga Alert–Ticket thất bại", "Giai đoạn {{failedAtStage}} — {{errorCode}}. Cần admin xử lý lại."),
            [NotificationTypeEnum.ChatEscalatedToAdmin] =
                ("Trao đổi được leo thang lên Admin", "Ticket {{ticketCode}}: Manager không phản hồi sau 30 phút."),

            [NotificationTypeEnum.BatteryAnomalyDetected] =
                ("Bất thường pin {{serialNumber}}", "Loại: {{anomalyType}} — Mức độ: {{severity}}."),
            [NotificationTypeEnum.BatteryAnomalyWarning] =
                ("Cảnh báo pin {{serialNumber}}", "{{anomalyType}}: giá trị {{actualValue}}{{unit}} vượt ngưỡng {{threshold}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyInfo] =
                ("Ghi nhận thay đổi trên pin {{serialNumber}}", "{{anomalyType}}: giá trị {{actualValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAlertEscalationPending] =
                ("Cảnh báo pin chưa được tiếp nhận", "Alert {{alertId}} chưa ai xác nhận sau {{minutesSinceDetection}} phút."),
            [NotificationTypeEnum.CascadeRiskHigh] =
                ("Rủi ro lan truyền cao tại {{siteName}}", "Pin {{serialNumber}} có nguy cơ ảnh hưởng các pin lân cận."),
            [NotificationTypeEnum.IotDeviceWentOffline] =
                ("Thiết bị IoT mất kết nối: {{deviceCode}}",
                 "Thiết bị \"{{displayName}}\" tại {{siteName}} mất heartbeat {{durationMinutes}} phút. Ảnh hưởng {{affectedBatteryCount}} pin."),

            [NotificationTypeEnum.EnvironmentalIncidentDetected] =
                ("Sự cố môi trường tại {{siteName}}", "Loại: {{incidentType}} — Mức độ: {{severity}}. Phát hiện lúc {{detectedAt}}."),
            [NotificationTypeEnum.EnvironmentalIncidentResolved] =
                ("Sự cố môi trường đã được xử lý", "Site {{siteName}} — sự cố {{incidentType}} đã kết thúc."),

            [NotificationTypeEnum.ChatCreated] =
                ("Trao đổi mới trên ticket {{ticketCode}}", "{{senderName}}: {{preview}}"),
            [NotificationTypeEnum.ChatMentioned] =
                ("Bạn được nhắc tới trong ticket {{ticketCode}}", "{{senderName}} đã nhắc tới bạn: {{preview}}"),
            [NotificationTypeEnum.ChatReacted] =
                ("Có phản hồi cho trao đổi của bạn", "{{senderName}} đã bày tỏ cảm xúc {{reaction}}."),
            [NotificationTypeEnum.ParticipantAdded] =
                ("Bạn được thêm vào ticket {{ticketCode}}", "Vai trò: {{role}}."),
            [NotificationTypeEnum.ParticipantRemoved] =
                ("Bạn đã rời ticket {{ticketCode}}", "Bạn không còn nhận cập nhật của ticket này."),
            [NotificationTypeEnum.ParticipantRoleChanged] =
                ("Vai trò của bạn trong ticket {{ticketCode}} đã đổi", "Vai trò mới: {{role}}."),

            [NotificationTypeEnum.AccountActivated] =
                ("Tài khoản đã được kích hoạt", "Chào {{fullName}}, tài khoản của bạn đã sẵn sàng sử dụng."),
            [NotificationTypeEnum.AdminInvite] =
                ("Lời mời tham gia hệ thống", "Bạn được {{inviterName}} mời làm {{role}}. Bấm liên kết để kích hoạt: {{activationLink}}"),
            [NotificationTypeEnum.System] =
                ("Thông báo hệ thống", "{{message}}"),
        };

    /// <summary>Bản tiếng Anh cho các type hướng Customer.</summary>
    private static readonly IReadOnlyDictionary<NotificationTypeEnum, (string Title, string Body)> English =
        new Dictionary<NotificationTypeEnum, (string, string)>
        {
            [NotificationTypeEnum.TicketCreated] =
                ("New ticket {{ticketCode}}", "Customer {{customerName}} created ticket {{ticketCode}}: {{title}}"),
            [NotificationTypeEnum.TicketStatusChanged] =
                ("Ticket {{ticketCode}} status changed", "From {{oldStatus}} to {{newStatus}}."),
            [NotificationTypeEnum.TicketResolved] =
                ("Ticket {{ticketCode}} resolved", "Ticket {{ticketCode}} has been resolved. Please confirm and rate."),
            [NotificationTypeEnum.TicketClosed] =
                ("Ticket {{ticketCode}} closed", "Thank you for using our service."),
            [NotificationTypeEnum.TicketApproved] =
                ("Ticket {{ticketCode}} resolution approved", "Please rate the service to close this ticket."),
            [NotificationTypeEnum.TicketRejected] =
                ("Ticket {{ticketCode}} rejected", "Reason: {{reason}}."),
            [NotificationTypeEnum.TicketReopened] =
                ("Ticket {{ticketCode}} reopened", "The customer was not satisfied. Reason: {{reason}}."),
            [NotificationTypeEnum.TicketRatingRequested] =
                ("Please rate ticket {{ticketCode}}", "Your feedback helps us serve you better."),
            [NotificationTypeEnum.AccountActivated] =
                ("Your account is active", "Hi {{fullName}}, your account is ready to use."),
            [NotificationTypeEnum.BatteryAnomalyWarning] =
                ("Battery warning {{serialNumber}}", "{{anomalyType}}: {{actualValue}}{{unit}} exceeded threshold {{threshold}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyInfo] =
                ("Battery update {{serialNumber}}", "{{anomalyType}}: current value {{actualValue}}{{unit}}."),
            [NotificationTypeEnum.EnvironmentalIncidentDetected] =
                ("Environmental incident at {{siteName}}", "Type: {{incidentType}} — Severity: {{severity}}. Detected at {{detectedAt}}."),
            [NotificationTypeEnum.EnvironmentalIncidentResolved] =
                ("Environmental incident resolved", "Site {{siteName}} — incident {{incidentType}} has ended."),
        };

    /// <summary>
    /// Dựng toàn bộ danh mục: mỗi ô của ma trận type × channel một dòng, cộng bản <c>en-US</c>
    /// cho các type hướng Customer.
    /// </summary>
    public static IReadOnlyList<Entry> Build(
        IReadOnlyDictionary<NotificationTypeEnum, NotificationChannelEnum[]> typeChannelMatrix)
    {
        var entries = new List<Entry>();

        foreach (var (type, channels) in typeChannelMatrix)
        {
            if (!Vietnamese.TryGetValue(type, out var vi))
                continue;

            foreach (var channel in channels)
            {
                var (title, body) = Adapt(channel, vi.Title, vi.Body);
                entries.Add(new Entry(type, channel, DefaultLocale, title, body));

                if (CustomerFacingTypes.Contains(type) && English.TryGetValue(type, out var en))
                {
                    var (enTitle, enBody) = Adapt(channel, en.Title, en.Body);
                    entries.Add(new Entry(type, channel, EnglishLocale, enTitle, enBody));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Biến tấu theo kênh. SMS tính tiền theo đoạn 160 ký tự và không có tiêu đề riêng, nên gộp
    /// tiêu đề vào thân và cắt ngắn — gửi nguyên văn bản email qua SMS là đốt tiền vô ích.
    /// </summary>
    private static (string Title, string Body) Adapt(NotificationChannelEnum channel, string title, string body)
    {
        if (channel != NotificationChannelEnum.Sms)
            return (title, body);

        var merged = $"{title}. {body}";
        return ("[Solar Battery]", merged.Length <= 300 ? merged : merged[..297] + "...");
    }
}
