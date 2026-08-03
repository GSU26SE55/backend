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

    /// <summary>
    /// Sprint 6.3 NOTI3-12 (#712) — số phiên bản trong cùng bộ ba (Type × Channel × Locale).
    ///
    /// Sửa template là **tạo bản mới** với version tăng dần, không ghi đè bản cũ. Nhờ vậy khi bản mới
    /// làm hỏng nội dung (thiếu placeholder, sai chính tả gửi cho hàng trăm khách) thì
    /// <b>rollback</b> chỉ là bật lại bản trước — dữ liệu cũ vẫn còn nguyên.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Bản đang dùng. Trong cùng bộ ba (Type × Channel × Locale) chỉ được có **đúng một** bản
    /// <c>IsActive = true</c>; dispatcher luôn lấy bản đó.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
