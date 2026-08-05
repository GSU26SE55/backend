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
/// **02/08/2026 — chỉ còn tiếng Việt.** Bản <c>en-US</c> và trường <c>Locale</c> đã bị gỡ: hệ thống
/// phục vụ tiếng Việt only, giữ thêm một bộ dịch chỉ tạo thứ phải bảo trì mà không ai đọc.
///
/// Tách khỏi <c>NotificationDataSeeder</c> để test bao (mọi ô của ma trận có template chưa) đọc được
/// danh mục mà không phải dựng DbContext.
/// </summary>
public static class NotificationTemplateCatalog
{
    /// <summary>Một dòng của danh mục.</summary>
    public readonly record struct Entry(
        NotificationTypeEnum Type,
        NotificationChannelEnum Channel,
        string Title,
        string Body);

    /// <summary>
    /// Nội dung tiếng Việt cho từng type — dùng chung cho mọi kênh, biến tấu theo kênh bên dưới.
    ///
    /// <para><b>03/08/2026 — viết lại toàn bộ tên biến.</b> Bộ cũ được soạn theo một hợp đồng payload
    /// <i>tưởng tượng</i>, không khớp khoá mà consumer thật sự ghi: <c>{{ticketCode}}</c> trong khi
    /// consumer ghi <c>code</c>, <c>{{serialNumber}}</c> trong khi consumer ghi
    /// <c>assetSerialNumber</c>, <c>{{threshold}}</c> trong khi consumer ghi <c>thresholdValue</c>,
    /// cùng hàng loạt biến không hề tồn tại (<c>customerName</c>, <c>slaDeadline</c>,
    /// <c>minutesRemaining</c>, <c>senderName</c>, <c>preview</c>, <c>displayName</c>…). Handlebars
    /// gặp biến lạ thì render ra rỗng chứ không báo lỗi, nên người nhận đọc phải "Ticket mới " —
    /// cụt đuôi — suốt nhiều tháng mà không ai hay.</para>
    ///
    /// <para>Mọi biến dưới đây phải nằm trong
    /// <c>NotificationTemplateVariables.AllowedFor(type)</c>; có test bao đối chiếu nên seed sai
    /// tên biến là CI đỏ ngay.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<NotificationTypeEnum, (string Title, string Body)> Vietnamese =
        new Dictionary<NotificationTypeEnum, (string, string)>
        {
            [NotificationTypeEnum.TicketCreated] =
                ("Ticket mới {{code}}", "Ticket {{code}} vừa được tạo, mức ưu tiên {{priority}}."),
            [NotificationTypeEnum.TicketAssigned] =
                ("Bạn được giao ticket {{code}}", "Mức ưu tiên {{priority}}. Mở ticket để xem chi tiết và hạn xử lý."),
            [NotificationTypeEnum.TicketStatusChanged] =
                ("Ticket {{code}} đổi trạng thái", "Từ {{oldStatusName}} sang {{newStatusName}}."),
            [NotificationTypeEnum.TicketResolved] =
                ("Ticket {{code}} đã xử lý xong", "Ticket {{code}} đã được xử lý. Vui lòng xác nhận và đánh giá."),
            [NotificationTypeEnum.TicketClosed] =
                ("Ticket {{code}} đã đóng", "Cảm ơn bạn đã sử dụng dịch vụ."),
            [NotificationTypeEnum.TicketEscalated] =
                ("Ticket {{code}} đã được leo thang", "Lý do: {{reason}}. Người phụ trách mới: {{staffName}}."),
            [NotificationTypeEnum.TicketApproved] =
                ("Kết quả xử lý ticket {{code}} đã được duyệt", "Vui lòng đánh giá chất lượng dịch vụ để đóng ticket."),
            [NotificationTypeEnum.TicketRejected] =
                ("Ticket {{code}} bị từ chối", "Lý do: {{reason}}."),
            [NotificationTypeEnum.TicketReopened] =
                ("Ticket {{code}} được mở lại", "Khách hàng chưa hài lòng với kết quả. Lý do: {{reopenReason}}."),
            [NotificationTypeEnum.TicketRatingRequested] =
                ("Mời đánh giá ticket {{code}}",
                 "Ticket đã hoàn tất. Còn {{daysUntilAutoClose}} ngày trước khi ticket tự đóng."),
            [NotificationTypeEnum.TicketMerged] =
                ("Ticket của bạn đã được gộp", "Nội dung đã chuyển sang ticket chính để xử lý tập trung."),

            // 03/08/2026 — hai event SLA nay đã mang theo `code`, nên nhắc được đúng ticket. Trước
            // đó payload chỉ có ticketId (GUID) nên tiêu đề buộc phải chung chung.
            [NotificationTypeEnum.SlaWarning] =
                ("Cảnh báo SLA: {{code}}", "Ticket {{code}} đã dùng {{percentage}}% thời gian SLA. Cần xử lý sớm."),
            [NotificationTypeEnum.SlaBreached] =
                ("VỠ SLA: {{code}}", "Ticket {{code}} mức ưu tiên {{priority}} đã quá hạn. Cần leo thang ngay."),
            [NotificationTypeEnum.IncidentDeclared] =
                ("Công bố sự cố nghiêm trọng", "Ticket {{code}} đã được công bố là sự cố nghiêm trọng."),
            [NotificationTypeEnum.AlertTicketSagaFailed] =
                ("Saga Alert–Ticket thất bại", "Giai đoạn {{failedAtStage}} — {{errorCode}}. Cần admin xử lý lại."),
            [NotificationTypeEnum.ChatEscalatedToAdmin] =
                ("Trao đổi được leo thang lên Admin", "Ticket {{ticketCode}}: Manager không phản hồi sau 30 phút."),

            // Dùng cặp {{anomalyTypeName}}/{{severityName}} — bản CHỮ. Hai khoá số
            // {{anomalyType}}/{{severity}} vẫn còn trong payload cho client lọc, nhưng in ra template
            // thì thành "Loại: 4 — Mức độ: 3", đúng thứ bản trước 03/08/2026 gửi cho khách.
            [NotificationTypeEnum.BatteryAnomalyDetected] =
                ("Bất thường pin {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — mức {{severityName}}. Giá trị đo {{actualValue}}{{unit}}, ngưỡng {{thresholdValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyWarning] =
                ("Cảnh báo pin {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — giá trị đo {{actualValue}}{{unit}}, ngưỡng {{thresholdValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyInfo] =
                ("Ghi nhận thay đổi trên pin {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — giá trị đo {{actualValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAlertEscalationPending] =
                ("Cảnh báo pin chưa được tiếp nhận", "Alert {{alertId}} chưa ai xác nhận sau {{minutesSinceDetection}} phút."),
            [NotificationTypeEnum.CascadeRiskHigh] =
                ("Rủi ro lan truyền cao",
                 "Điểm rủi ro {{cascadeRiskScore}} — pin có nguy cơ ảnh hưởng các pin lân cận."),
            [NotificationTypeEnum.IotDeviceWentOffline] =
                ("Thiết bị IoT mất kết nối: {{deviceCode}}",
                 "Thiết bị {{deviceCode}} mất heartbeat từ {{lastSeenAt}}. Ảnh hưởng {{affectedBatteryCount}} pin."),

            [NotificationTypeEnum.EnvironmentalIncidentDetected] =
                ("Sự cố môi trường tại {{siteName}}", "Loại: {{incidentType}} — Mức độ: {{severity}}. Phát hiện lúc {{detectedAt}}."),
            // Consumer của type này KHÔNG ghi payload, nên chỉ được dùng biến builtin.
            [NotificationTypeEnum.EnvironmentalIncidentResolved] =
                ("Sự cố môi trường đã được xử lý", "Sự cố môi trường tại site của bạn đã kết thúc."),

            // ChatCreated là luồng realtime: consumer đã dựng preview "sender: body" trong
            // Title/Body builtin. Giữ nguyên hai field này để banner/bubble hiện đúng tin thật.
            [NotificationTypeEnum.ChatCreated] =
                ("{{Title}}", "{{Body}}"),
            [NotificationTypeEnum.ChatMentioned] =
                ("Bạn được nhắc tới trong một trao đổi", "Mở ticket để xem nội dung nhắc tới bạn."),
            [NotificationTypeEnum.ChatReacted] =
                ("Có phản hồi cho trao đổi của bạn", "Ai đó đã bày tỏ cảm xúc {{reactionType}}."),
            [NotificationTypeEnum.ParticipantAdded] =
                ("Bạn được thêm vào một ticket", "Vai trò của bạn: {{newType}}."),
            [NotificationTypeEnum.ParticipantRemoved] =
                ("Bạn đã rời ticket", "Bạn không còn nhận cập nhật của ticket này."),
            [NotificationTypeEnum.ParticipantRoleChanged] =
                ("Vai trò của bạn trong ticket đã đổi", "Từ {{oldType}} sang {{newType}}."),

            [NotificationTypeEnum.AccountActivated] =
                ("Tài khoản đã được kích hoạt", "Tài khoản của bạn đã sẵn sàng sử dụng. Vai trò: {{role}}."),
            [NotificationTypeEnum.BlogGenerationCompleted] =
                ("Bài viết đã tạo xong", "Bài viết bạn yêu cầu đã sinh xong và sẵn sàng để duyệt."),
            [NotificationTypeEnum.BlogGenerationFailed] =
                ("Tạo bài viết thất bại", "Không sinh được bài viết. Vui lòng thử lại hoặc báo quản trị viên."),

            // System phải là template CHUYỂN TIẾP NGUYÊN VĂN — cả tiêu đề lẫn thân đều lấy biến
            // builtin, tức chính nội dung admin gõ lúc gửi hàng loạt.
            //
            // Đặt tiêu đề cố định ("Thông báo hệ thống") là SAI: từ 03/08/2026 kênh InApp ghi ngược
            // nội dung đã render vào dòng notification, nên tiêu đề cố định sẽ ĐÈ MẤT tiêu đề admin
            // vừa nhập — người nhận mở feed lên chỉ thấy "Thông báo hệ thống" thay vì
            // "Bảo trì hệ thống 22:00". Với System thì nội dung CHÍNH LÀ thông điệp, không có gì để
            // khuôn mẫu hoá.
            [NotificationTypeEnum.System] =
                ("{{Title}}", "{{Body}}"),
        };

    /// <summary>Dựng toàn bộ danh mục: mỗi ô của ma trận type × channel đúng một dòng tiếng Việt.</summary>
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
                entries.Add(new Entry(type, channel, title, body));
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
