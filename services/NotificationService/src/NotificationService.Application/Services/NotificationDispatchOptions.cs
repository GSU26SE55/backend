using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.2 NOTI-01 (#672) + NOTI-14 (#685) — cấu hình tầng dispatch.
///
/// NOTI-14 yêu cầu "cân nhắc đưa <c>TypeChannelMatrix</c> (hardcode trong dispatcher) ra config".
/// Ma trận + danh sách critical type nay bind từ section <c>Notification:Dispatch</c>; nếu config
/// không khai báo thì rơi về default dưới đây (đúng bằng giá trị hardcode cũ + bổ sung các type
/// mới của Sprint Chat / Sprint Bonus / Sprint 6.2 vốn bị thiếu nên rơi vào fallback InApp-only).
///
/// Ví dụ appsettings:
/// <code>
/// "Notification": {
///   "Dispatch": {
///     "PollIntervalSeconds": 5,
///     "BatchSize": 100,
///     "MaxAttempts": 5,
///     "TypeChannelMatrix": { "SlaBreached": [ "InApp", "Push", "Email", "Sms" ] },
///     "CriticalTypes": [ "SlaBreached", "IncidentDeclared" ]
///   }
/// }
/// </code>
/// </summary>
public class NotificationDispatchOptions
{
    public const string SectionName = "Notification:Dispatch";

    /// <summary>Bật/tắt worker dispatch. False = quay lại hành vi cũ (chỉ ghi DB, không gửi).</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 100;

    /// <summary>Số lần thử tối đa trước khi đánh dấu Failed vĩnh viễn.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Backoff cơ sở (giây); lần thử thứ n hoãn <c>BaseBackoffSeconds * 2^(n-1)</c>.</summary>
    public int BaseBackoffSeconds { get; set; } = 30;

    /// <summary>Trần backoff (giây) để không hoãn quá lâu.</summary>
    public int MaxBackoffSeconds { get; set; } = 900;

    /// <summary>
    /// Ưu tiên render Title/Body từ bảng <c>notification_templates</c> nếu có template active khớp
    /// (Type × Channel × Locale); không có thì dùng Title/Body inline mà consumer đã ghi sẵn.
    /// Đây là "1 pattern" chốt cho NOTI-14: DB template thắng, inline là fallback.
    /// </summary>
    public bool UseDbTemplates { get; set; } = true;

    public string DefaultLocale { get; set; } = "vi-VN";

    /// <summary>Override ma trận Type → Channel. Key/value là TÊN enum (không phân biệt hoa thường).</summary>
    public Dictionary<string, string[]> TypeChannelMatrix { get; set; } = new();

    /// <summary>Override danh sách type luôn bypass quiet hours. Null/rỗng = dùng default.</summary>
    public string[] CriticalTypes { get; set; } = Array.Empty<string>();

    // ---------- Default (bằng đúng hardcode cũ, cộng type còn thiếu) ----------

    public static readonly NotificationChannelEnum[] DefaultChannels = [NotificationChannelEnum.InApp];

    /// <summary>overall.md §3.4 — Type → Channel matrix.</summary>
    public static readonly IReadOnlyDictionary<NotificationTypeEnum, NotificationChannelEnum[]> DefaultTypeChannelMatrix =
        new Dictionary<NotificationTypeEnum, NotificationChannelEnum[]>
        {
            [NotificationTypeEnum.TicketCreated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.TicketAssigned] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.TicketStatusChanged] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.TicketResolved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.TicketClosed] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.TicketEscalated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.SlaWarning] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.SlaBreached] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
            [NotificationTypeEnum.BatteryAnomalyDetected] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.EnvironmentalIncidentDetected] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
            [NotificationTypeEnum.EnvironmentalIncidentResolved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.IncidentDeclared] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
            [NotificationTypeEnum.AccountActivated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Email],
            [NotificationTypeEnum.AdminInvite] = [NotificationChannelEnum.Email],
            [NotificationTypeEnum.BatteryAlertEscalationPending] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.AlertTicketSagaFailed] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.IotDeviceWentOffline] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],

            // Bổ sung Sprint 6.2 — trước đây thiếu nên DispatchAsync rơi về fallback InApp-only.
            [NotificationTypeEnum.CascadeRiskHigh] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.ChatCreated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.ChatMentioned] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.ChatReacted] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.ParticipantAdded] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.ParticipantRemoved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.ParticipantRoleChanged] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.ChatEscalatedToAdmin] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.TicketApproved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.TicketRejected] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
            [NotificationTypeEnum.TicketReopened] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.TicketRatingRequested] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.BatteryAnomalyWarning] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
            [NotificationTypeEnum.BatteryAnomalyInfo] = [NotificationChannelEnum.InApp],

            [NotificationTypeEnum.System] = [NotificationChannelEnum.InApp],
        };

    /// <summary>overall.md §3.4 — type luôn bypass quiet hours.</summary>
    public static readonly IReadOnlySet<NotificationTypeEnum> DefaultCriticalTypes =
        new HashSet<NotificationTypeEnum>
        {
            NotificationTypeEnum.EnvironmentalIncidentDetected,
            NotificationTypeEnum.IncidentDeclared,
            NotificationTypeEnum.BatteryAlertEscalationPending,
            NotificationTypeEnum.AlertTicketSagaFailed,
            NotificationTypeEnum.SlaBreached,
            // Sprint 6.2: escalation chat lên Admin là critical (saga đã chờ Manager 30' không ACK).
            NotificationTypeEnum.ChatEscalatedToAdmin,
        };

    /// <summary>Ma trận đã merge config lên default (config thắng cho từng key).</summary>
    public IReadOnlyDictionary<NotificationTypeEnum, NotificationChannelEnum[]> ResolveTypeChannelMatrix()
    {
        var merged = new Dictionary<NotificationTypeEnum, NotificationChannelEnum[]>(DefaultTypeChannelMatrix);

        foreach (var (typeName, channelNames) in TypeChannelMatrix)
        {
            if (!Enum.TryParse<NotificationTypeEnum>(typeName, ignoreCase: true, out var type))
                continue;

            var channels = channelNames
                .Select(c => Enum.TryParse<NotificationChannelEnum>(c, ignoreCase: true, out var ch) ? ch : (NotificationChannelEnum?)null)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .Distinct()
                .ToArray();

            if (channels.Length > 0)
                merged[type] = channels;
        }

        return merged;
    }

    /// <summary>Danh sách critical type — config override hoàn toàn nếu có khai báo.</summary>
    public IReadOnlySet<NotificationTypeEnum> ResolveCriticalTypes()
    {
        if (CriticalTypes.Length == 0)
            return DefaultCriticalTypes;

        var parsed = CriticalTypes
            .Select(t => Enum.TryParse<NotificationTypeEnum>(t, ignoreCase: true, out var type) ? type : (NotificationTypeEnum?)null)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToHashSet();

        return parsed.Count > 0 ? parsed : DefaultCriticalTypes;
    }
}
