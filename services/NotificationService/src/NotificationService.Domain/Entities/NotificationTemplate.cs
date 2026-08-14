using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Template render Title/Body cho 1 (Type × Channel). Dispatcher load template theo
/// (Type, Channel) rồi merge với payload JSON từ event.
///
/// <para><b>02/08/2026 — bỏ hẳn <c>Locale</c>.</b> Hệ thống chỉ phục vụ tiếng Việt, nên cột locale
/// và toàn bộ nhánh chọn ngôn ngữ (bản <c>en-US</c> trong catalog, <c>AccountReadModel.PreferredLocale</c>,
/// <c>NotificationDispatchOptions.DefaultLocale</c>) là chi phí bảo trì không đổi lấy giá trị nào.
/// Khoá nghiệp vụ rút từ bộ ba (Type × Channel × Locale) xuống cặp (Type × Channel).</para>
/// </summary>
public class NotificationTemplate : AuditableEntity
{
    public NotificationTypeEnum Type { get; set; }

    public NotificationChannelEnum Channel { get; set; }

    /// <summary>Title template (Handlebars syntax {{var}}).</summary>
    public string TitleTemplate { get; set; } = string.Empty;

    /// <summary>Body template (Handlebars syntax).</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Sprint 6.3 NOTI3-12 (#712) — số phiên bản trong cùng cặp (Type × Channel).
    ///
    /// Sửa template là **tạo bản mới** với version tăng dần, không ghi đè bản cũ. Nhờ vậy khi bản mới
    /// làm hỏng nội dung (thiếu placeholder, sai chính tả gửi cho hàng trăm khách) thì
    /// <b>rollback</b> chỉ là bật lại bản trước — dữ liệu cũ vẫn còn nguyên.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Bản đang dùng. Trong cùng cặp (Type × Channel) chỉ được có **đúng một** bản
    /// <c>IsActive = true</c>; dispatcher luôn lấy bản đó.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
