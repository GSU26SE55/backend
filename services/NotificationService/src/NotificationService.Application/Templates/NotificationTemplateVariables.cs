using NotificationService.Domain.Enums;

namespace NotificationService.Application.Templates;

/// <summary>
/// Danh mục biến hợp lệ cho từng <see cref="NotificationTypeEnum"/> — **hợp đồng** giữa consumer
/// (bên ghi <c>PayloadJson</c>) và template (bên đọc <c>{{bien}}</c>).
///
/// <para><b>Vì sao phải có file này.</b> Handlebars gặp biến không tồn tại thì render ra chuỗi rỗng
/// chứ không ném lỗi. Nghĩa là template gọi sai tên biến vẫn lưu được, vẫn gửi được, chỉ là người
/// nhận đọc phải câu cụt. Không có gì trong hệ thống phát hiện ra — không log, không metric, không
/// test. Đây chính là cách <c>{{ticketCode}}</c> tồn tại hàng tháng trong khi consumer ghi khoá
/// <c>code</c>, và <c>{{serialNumber}}</c> tồn tại trong khi consumer ghi
/// <c>assetSerialNumber</c>.</para>
///
/// <para><b>Vì sao khai bằng tay chứ không sinh tự động.</b> Consumer dựng payload bằng anonymous
/// object (<c>JsonSerializer.Serialize(new { ... })</c>), không có kiểu để phản chiếu lúc chạy. Bù
/// lại có <c>NotificationTemplateVariableCatalogTests</c> đối chiếu danh mục này với chính chuỗi
/// JSON mà consumer sinh ra, nên sửa consumer mà quên sửa đây là test đỏ.</para>
///
/// <para><b>Khi thêm/sửa consumer:</b> cập nhật đúng một dòng ở đây. Đó là toàn bộ nghĩa vụ.</para>
/// </summary>
public static class NotificationTemplateVariables
{
    /// <summary>
    /// Sáu biến <c>NotificationDispatcher.BuildTemplateModel</c> luôn nạp, không phụ thuộc payload.
    /// Mọi type đều dùng được.
    /// </summary>
    public static readonly IReadOnlySet<string> Builtin =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Title", "Body", "EntityType", "EntityId", "UserId", "CreatedAt",
        };

    // Hai mảng dùng chung phải khai TRƯỚC PayloadKeys: static field khởi tạo theo đúng thứ tự khai
    // báo, đặt sau thì lúc PayloadKeys chạy chúng vẫn còn null.
    private static readonly string[] BatteryAnomalyKeys =
    [
        "alertId", "batteryAssetId", "assetSerialNumber", "customerId", "anomalyType",
        "severity", "actualValue", "thresholdValue", "unit", "detectedAt", "screen",
        // 03/08/2026 — bản chữ của hai khoá số ngay trên. Template phải dùng cặp *Name này; dùng
        // `anomalyType`/`severity` sẽ in ra con số trần ("Loại: 4 — Mức độ: 3").
        "anomalyTypeName", "severityName",
    ];

    private static readonly string[] ParticipantKeys = ["ticketId", "oldType", "newType"];

    /// <summary>
    /// Khoá payload theo từng type — chép đúng tên khoá trong <c>JsonSerializer.Serialize(new {...})</c>
    /// của consumer tương ứng. Type không có mặt ở đây = consumer không ghi payload, template chỉ
    /// được dùng <see cref="Builtin"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<NotificationTypeEnum, string[]> PayloadKeys =
        new Dictionary<NotificationTypeEnum, string[]>
        {
            // ── Ticket ────────────────────────────────────────────────────────────────────────
            [NotificationTypeEnum.TicketCreated] =
                ["ticketId", "code", "customerId", "priority", "screen"],
            [NotificationTypeEnum.TicketAssigned] =
                ["ticketId", "code", "customerId", "priority", "staffId", "screen"],
            [NotificationTypeEnum.TicketStatusChanged] =
                ["ticketId", "code", "oldStatus", "newStatus", "oldStatusName", "newStatusName", "screen"],
            [NotificationTypeEnum.TicketResolved] =
                ["ticketId", "code", "customerId", "resolvedByStaffId", "screen"],
            [NotificationTypeEnum.TicketClosed] =
                ["ticketId", "code", "closedAt", "isAutoClosed", "rating", "screen"],
            [NotificationTypeEnum.TicketEscalated] =
                ["ticketId", "code", "reason", "note", "staffId", "staffName", "screen"],
            [NotificationTypeEnum.TicketApproved] =
                ["ticketId", "code", "approvedAt", "screen"],
            [NotificationTypeEnum.TicketRejected] =
                ["ticketId", "code", "reason", "isClosedRejected", "rejectedAt", "screen"],
            [NotificationTypeEnum.TicketReopened] =
                ["ticketId", "code", "reopenReason", "reopenCount", "reopenedAt", "screen"],
            [NotificationTypeEnum.TicketRatingRequested] =
                ["ticketId", "code", "approvedAt", "daysPending", "daysUntilAutoClose", "screen"],
            [NotificationTypeEnum.TicketMerged] =
                ["sourceTicketId", "masterTicketId"],

            // ── SLA ───────────────────────────────────────────────────────────────────────────
            // `code` thêm 03/08/2026 — trước đó hai loại này chỉ có TicketId (GUID) nên template
            // không nhắc được ticket nào, dù đây đúng là loại cần biết ngay để mở ra xử lý.
            [NotificationTypeEnum.SlaWarning] =
                ["ticketId", "code", "staffId", "percentage", "warningAt", "screen"],
            [NotificationTypeEnum.SlaBreached] =
                ["ticketId", "code", "priority", "priorityTier", "breachedAt", "screen"],

            // ── Pin / cảnh báo ────────────────────────────────────────────────────────────────
            // Ba type dùng CHUNG payload vì cùng do consumer họ BatteryAnomaly sinh ra, chỉ khác
            // mức độ nghiêm trọng.
            [NotificationTypeEnum.BatteryAnomalyDetected] = BatteryAnomalyKeys,
            [NotificationTypeEnum.BatteryAnomalyWarning] = BatteryAnomalyKeys,
            [NotificationTypeEnum.BatteryAnomalyInfo] = BatteryAnomalyKeys,
            [NotificationTypeEnum.BatteryAlertEscalationPending] =
                ["alertId", "batteryAssetId", "anomalyType", "severity", "minutesSinceDetection"],
            [NotificationTypeEnum.CascadeRiskHigh] =
                ["batteryAssetId", "siteId", "cascadeRiskScore", "relatedTicketId", "screen"],
            [NotificationTypeEnum.AlertTicketSagaFailed] =
                ["alertId", "ticketId", "correlationId", "errorCode", "failedAt", "failedAtStage"],

            // ── Môi trường / sự cố ────────────────────────────────────────────────────────────
            [NotificationTypeEnum.EnvironmentalIncidentDetected] =
                ["incidentId", "siteId", "siteName", "incidentType", "severity",
                 "alertId", "customerId", "detectedAt", "description", "screen"],
            [NotificationTypeEnum.IncidentDeclared] =
                ["ticketId", "code", "declaredByUserId", "screen"],

            // ── IoT ───────────────────────────────────────────────────────────────────────────
            [NotificationTypeEnum.IotDeviceWentOffline] =
                ["iotDeviceId", "deviceCode", "siteId", "alertId",
                 "lastSeenAt", "offlineDurationSeconds", "affectedBatteryCount"],

            // ── Trao đổi (chat) ───────────────────────────────────────────────────────────────
            [NotificationTypeEnum.ChatCreated] = ["chatId", "ticketId"],
            [NotificationTypeEnum.ChatMentioned] = ["chatId", "ticketId", "isGroupMention"],
            [NotificationTypeEnum.ChatReacted] = ["chatId", "ticketId", "reactionType"],
            [NotificationTypeEnum.ChatEscalatedToAdmin] =
                ["chatId", "ticketId", "ticketCode", "managerUserId", "screen"],
            [NotificationTypeEnum.ParticipantAdded] = ParticipantKeys,
            [NotificationTypeEnum.ParticipantRemoved] = ParticipantKeys,
            [NotificationTypeEnum.ParticipantRoleChanged] = ParticipantKeys,

            // ── Tài khoản ─────────────────────────────────────────────────────────────────────
            [NotificationTypeEnum.AccountActivated] =
                ["accountId", "creationSource", "role", "screen"],

            // ── Blog ──────────────────────────────────────────────────────────────────────────
            [NotificationTypeEnum.BlogGenerationCompleted] = ["blogPostId"],
            [NotificationTypeEnum.BlogGenerationFailed] = ["blogPostId"],

            // ── Hệ thống ──────────────────────────────────────────────────────────────────────
            // Sprint 6.4: admin gửi hàng loạt tự nhập payload, nên KHÔNG có khoá nào chắc chắn tồn
            // tại. Bản tin gom (digest) thì luôn có 5 khoá dưới đây.
            [NotificationTypeEnum.System] =
                ["digest", "count", "from", "to", "notificationIds"],

            // EnvironmentalIncidentResolved và AdminInvite cố ý vắng mặt: consumer không ghi
            // payload nào (AdminInvite hiện chưa có consumer). Template của chúng chỉ được dùng
            // biến Builtin.
        };

    /// <summary>
    /// Mọi biến template của <paramref name="type"/> được phép dùng: <see cref="Builtin"/> cộng
    /// khoá payload riêng của type đó.
    /// </summary>
    public static IReadOnlySet<string> AllowedFor(NotificationTypeEnum type)
    {
        var allowed = new HashSet<string>(Builtin, StringComparer.OrdinalIgnoreCase);
        if (PayloadKeys.TryGetValue(type, out var keys))
            allowed.UnionWith(keys);
        return allowed;
    }

    /// <summary>
    /// Chỉ các khoá payload (không kèm <see cref="Builtin"/>) — dùng để dựng dữ liệu mẫu cho màn
    /// hình xem trước, và để FE hiện danh sách biến gợi ý.
    /// </summary>
    public static IReadOnlyList<string> PayloadKeysFor(NotificationTypeEnum type) =>
        PayloadKeys.TryGetValue(type, out var keys) ? keys : Array.Empty<string>();

    /// <summary>Các type đã khai payload — phục vụ test bao và endpoint tra cứu.</summary>
    public static IEnumerable<NotificationTypeEnum> DeclaredTypes => PayloadKeys.Keys;
}
