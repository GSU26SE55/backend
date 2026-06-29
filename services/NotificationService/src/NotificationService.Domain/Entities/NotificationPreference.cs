using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Cấu hình kênh + quiet hours của 1 user. Mỗi user có 1 record (1-1 với Account).
/// Dispatcher sẽ đọc record này trước khi quyết định gửi qua channel nào.
/// </summary>
public class NotificationPreference : AuditableEntity
{
    public Guid UserId { get; set; }

    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = false;
    public bool InAppEnabled { get; set; } = true;

    /// <summary>Bắt đầu khoảng "không làm phiền" (giờ địa phương HH:mm). Null = không quiet hours.</summary>
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>Timezone IANA (vd "Asia/Ho_Chi_Minh") — cho FE hiển thị + dispatcher tính quiet hours.</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    public NotificationFrequencyEnum Frequency { get; set; } = NotificationFrequencyEnum.Immediate;

    // Chat-specific notification preferences (#570)
    public bool NotifyOnChat { get; set; } = true;
    public bool NotifyOnMention { get; set; } = true;
    public bool NotifyOnReaction { get; set; } = false;

    /// <summary>Null = immediate; 5/15/30 = digest window in minutes.</summary>
    public int? DigestWindowMinutes { get; set; }
}
