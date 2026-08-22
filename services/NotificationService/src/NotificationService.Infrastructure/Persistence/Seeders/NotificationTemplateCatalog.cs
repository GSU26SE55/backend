using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Sprint 6.3 NOTI3-12 (#712) — bộ template chuẩn, phủ mọi type × channel có trong
/// <c>NotificationDispatchOptions.DefaultTypeChannelMatrix</c>.
///
/// **Vấn đề trước sprint này:** chỉ một phần nhỏ type có template trong DB. Với các type còn lại, dispatcher
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
                ("New ticket {{code}}", "Ticket {{code}} was just created, priority {{priority}}."),
            [NotificationTypeEnum.TicketAssigned] =
                ("You have been assigned ticket {{code}}", "Priority {{priority}}. Open the ticket to view details and the deadline."),
            [NotificationTypeEnum.TicketWorkStarted] =
                ("Work started on ticket {{code}}", "Work began at {{startedAtUtc}}. Open the ticket to view progress."),
            [NotificationTypeEnum.TicketScheduleChanged] =
                ("Schedule changed for ticket {{code}}", "The next work time is {{scheduledStartAtUtc}}."),
            [NotificationTypeEnum.PeriodicMaintenanceReminder] =
                ("Periodic maintenance for ticket {{code}}", "Maintenance is due at {{maintenanceDueAtUtc}}. Please arrange a visit schedule."),
            [NotificationTypeEnum.PeriodicMaintenanceScheduleChanged] =
                ("Maintenance schedule changed for {{code}}", "The maintenance visit is scheduled for {{scheduledStartAtUtc}}."),
            [NotificationTypeEnum.TicketStatusChanged] =
                ("Ticket {{code}} status changed", "From {{oldStatusName}} to {{newStatusName}}."),
            [NotificationTypeEnum.TicketResolved] =
                ("Ticket {{code}} has been resolved", "Ticket {{code}} has been resolved. Please confirm and rate it."),
            [NotificationTypeEnum.TicketClosed] =
                ("Ticket {{code}} closed", "Thank you for using our service."),
            [NotificationTypeEnum.TicketEscalated] =
                ("Ticket {{code}} has been escalated", "Reason: {{reason}}. New assignee: {{staffName}}."),
            [NotificationTypeEnum.TicketApproved] =
                ("Resolution for ticket {{code}} has been approved", "Please rate the service quality to close the ticket."),
            [NotificationTypeEnum.TicketRejected] =
                ("Ticket {{code}} rejected", "Reason: {{reason}}."),
            [NotificationTypeEnum.TicketReopened] =
                ("Ticket {{code}} reopened", "The customer was not satisfied with the resolution. Reason: {{reopenReason}}."),
            [NotificationTypeEnum.TicketRatingRequested] =
                ("Please rate ticket {{code}}",
                 "The ticket has been completed. {{daysUntilRatingDeadline}} day(s) remain in the rating window."),
            [NotificationTypeEnum.TicketMerged] =
                ("Your ticket has been merged", "The content has been moved to the primary ticket for centralized handling."),

            // 03/08/2026 — hai event SLA nay đã mang theo `code`, nên nhắc được đúng ticket. Trước
            // đó payload chỉ có ticketId (GUID) nên tiêu đề buộc phải chung chung.
            [NotificationTypeEnum.SlaWarning] =
                ("SLA warning: {{code}}", "Ticket {{code}} has used {{percentage}}% of its SLA time. Needs prompt action."),
            [NotificationTypeEnum.SlaBreached] =
                ("SLA BREACHED: {{code}}", "Ticket {{code}} (priority {{priority}}) is overdue. Immediate escalation required."),
            [NotificationTypeEnum.SlaAutoResumed] =
                ("SLA automatically resumed: {{code}}", "The maximum pause duration ended at {{resumedAt}}. Action is required."),
            [NotificationTypeEnum.IncidentDeclared] =
                ("Critical incident declared", "Ticket {{code}} has been declared a critical incident."),
            [NotificationTypeEnum.AlertTicketSagaFailed] =
                ("Alert–Ticket saga failed", "Stage {{failedAtStage}} — {{errorCode}}. Requires admin intervention."),
            [NotificationTypeEnum.ChatEscalatedToAdmin] =
                ("Conversation escalated to Admin", "Ticket {{ticketCode}}: Manager has not responded after 30 minutes."),

            // Dùng cặp {{anomalyTypeName}}/{{severityName}} — bản CHỮ. Hai khoá số
            // {{anomalyType}}/{{severity}} vẫn còn trong payload cho client lọc, nhưng in ra template
            // thì thành "Loại: 4 — Mức độ: 3", đúng thứ bản trước 03/08/2026 gửi cho khách.
            [NotificationTypeEnum.BatteryAnomalyDetected] =
                ("Battery anomaly {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — level {{severityName}}. Measured value {{actualValue}}{{unit}}, threshold {{thresholdValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyWarning] =
                ("Battery warning {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — measured value {{actualValue}}{{unit}}, threshold {{thresholdValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAnomalyInfo] =
                ("Change recorded on battery {{assetSerialNumber}}",
                 "{{anomalyTypeName}} — measured value {{actualValue}}{{unit}}."),
            [NotificationTypeEnum.BatteryAlertEscalationPending] =
                // Nêu tên pin, không nêu id alert: người nhận tra theo pin, và màn Alerts cũng
                // lọc theo pin chứ không theo id. alertId vẫn nằm trong payload để deep link.
                ("Battery alert not yet acknowledged",
                 "The alert on battery {{assetSerialNumber}} has not been acknowledged after {{minutesSinceDetection}} minute(s)."),
            [NotificationTypeEnum.CascadeRiskHigh] =
                ("High cascade risk",
                 "Risk score {{cascadeRiskScore}} — this battery may affect nearby batteries."),
            [NotificationTypeEnum.IotDeviceWentOffline] =
                ("IoT device offline: {{deviceCode}}",
                 "Device {{deviceCode}} has missed heartbeats since {{lastSeenAt}}. Affects {{affectedBatteryCount}} battery/batteries."),
            [NotificationTypeEnum.IotDeviceRecovered] =
                ("IoT device recovered: {{deviceCode}}",
                 "Device {{deviceCode}} is stable again and its offline incident has been resolved."),
            [NotificationTypeEnum.IotDeviceAutoDecommissioned] =
                ("IoT device disabled: {{deviceCode}}",
                 "Device {{deviceCode}} submitted {{rejectedReadingCount}} invalid readings and was disabled for safety."),

            [NotificationTypeEnum.EnvironmentalIncidentDetected] =
                ("Environmental incident at {{siteName}}", "Type: {{incidentType}} — Severity: {{severity}}. Detected at {{detectedAt}}."),
            // Consumer của type này KHÔNG ghi payload, nên chỉ được dùng biến builtin.
            [NotificationTypeEnum.EnvironmentalIncidentResolved] =
                ("Environmental incident resolved", "The environmental incident at your site has ended."),

            // ── Guide (KB) review ────────────────────────────────────────────────────────────
            // {{articleTitle}}/{{requestedByName}} đến từ PayloadJson của consumer. Không dùng
            // {{changeDescription}} trong template: nó do người sửa tự nhập, dài ngắn tuỳ ý và
            // consumer đã cắt ngắn khi dựng Body builtin — để nguyên ở payload cho ai cần tra.
            [NotificationTypeEnum.KbArticleReviewRequested] =
                ("Guide article awaiting approval",
                 "{{requestedByName}} submitted \"{{articleTitle}}\" for approval."),
            [NotificationTypeEnum.KbArticleReviewApproved] =
                ("Guide article change approved",
                 "{{decidedByName}} approved your change to \"{{articleTitle}}\". It is now live."),
            [NotificationTypeEnum.KbArticleReviewRejected] =
                ("Guide article change rejected",
                 "{{decidedByName}} rejected your change to \"{{articleTitle}}\". Reason: {{rejectReason}}."),

            // ChatCreated là luồng realtime: consumer đã dựng preview "sender: body" trong
            // Title/Body builtin. Giữ nguyên hai field này để banner/bubble hiện đúng tin thật.
            [NotificationTypeEnum.ChatCreated] =
                ("{{Title}}", "{{Body}}"),
            [NotificationTypeEnum.ChatMentioned] =
                ("You were mentioned in a conversation", "Open the ticket to see what you were mentioned in."),
            [NotificationTypeEnum.ChatReacted] =
                ("Someone reacted to your message", "Someone reacted with {{reactionType}}."),
            [NotificationTypeEnum.ParticipantAdded] =
                ("You were added to a ticket", "Your role: {{newType}}."),
            [NotificationTypeEnum.ParticipantRemoved] =
                ("You left the ticket", "You will no longer receive updates for this ticket."),
            [NotificationTypeEnum.ParticipantRoleChanged] =
                ("Your role in the ticket has changed", "From {{oldType}} to {{newType}}."),

            [NotificationTypeEnum.AccountActivated] =
                ("Account activated", "Your account is now ready to use. Role: {{role}}."),
            [NotificationTypeEnum.BlogGenerationCompleted] =
                ("Article generation completed", "The article you requested has been generated and is ready for review."),
            [NotificationTypeEnum.BlogGenerationFailed] =
                ("Article generation failed", "Failed to generate the article. Please try again or contact an administrator."),

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
