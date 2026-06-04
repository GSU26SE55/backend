using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Template render Title/Body cho 1 (Type × Channel × Locale). Dispatcher load template
/// theo (Type, Channel, Locale) rồi merge với payload JSON từ event.
/// </summary>
public class NotificationTemplate : AuditableEntity
{
    public NotificationTypeEnum Type { get; set; }

    public NotificationChannelEnum Channel { get; set; }

    /// <summary>Locale BCP-47 (vd "vi-VN", "en-US"). Default "vi-VN".</summary>
    public string Locale { get; set; } = "vi-VN";

    /// <summary>Title template (Handlebars syntax {{var}}).</summary>
    public string TitleTemplate { get; set; } = string.Empty;

    /// <summary>Body template (Handlebars syntax).</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
