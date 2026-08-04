using NotificationService.Domain.Enums;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>
/// Một dòng trong danh sách quản trị template (<c>GET /api/admin/notification-templates</c>).
///
/// <para><b>02/08/2026 — <see cref="Type"/> và <see cref="Channel"/> trả về dạng SỐ.</b> Trước đây
/// trả TÊN enum (<c>"SlaBreached"</c>, <c>"Email"</c>) để người vận hành đọc được bằng mắt, nhưng tên
/// enum là tiếng Anh — dán thẳng lên màn hình tiếng Việt thì sai. Nay BE trả số, FE tự ánh xạ sang
/// nhãn tiếng Việt; đây cũng là cách mọi DTO notification khác đang làm
/// (xem <see cref="NotificationDto"/>), hết ngoại lệ.</para>
///
/// <para><b>02/08/2026 — bỏ <c>Locale</c>:</b> hệ thống tiếng Việt only.</para>
/// </summary>
public class NotificationTemplateDto
{
    public Guid Id { get; set; }

    /// <summary>Giá trị số của <c>NotificationTypeEnum</c>.</summary>
    public NotificationTypeEnum Type { get; set; }

    /// <summary>Giá trị số của <c>NotificationChannelEnum</c>.</summary>
    public NotificationChannelEnum Channel { get; set; }

    /// <summary>Số phiên bản trong cùng cặp (Type × Channel), bắt đầu từ 1.</summary>
    public int Version { get; set; }

    /// <summary>Bản dispatcher đang dùng — mỗi cặp chỉ có đúng một.</summary>
    public bool IsActive { get; set; }

    public string TitleTemplate { get; set; } = string.Empty;

    public string BodyTemplate { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
