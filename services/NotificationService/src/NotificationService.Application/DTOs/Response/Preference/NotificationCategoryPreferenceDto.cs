using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Preference;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — một dòng của ma trận nhóm × kênh.
/// </summary>
public class NotificationCategoryPreferenceDto
{
    /// <summary>Mã nhóm (số) — ổn định để FE lưu.</summary>
    public NotificationCategoryEnum Category { get; set; }

    /// <summary>Tên nhóm dạng chuỗi — FE hiển thị mà không cần bảng tra cứu riêng.</summary>
    public string CategoryName { get; set; } = string.Empty;

    public bool PushEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    public bool InAppEnabled { get; set; }

    /// <summary>
    /// <c>false</c> = người dùng chưa tuỳ chỉnh nhóm này; giá trị trả về là mặc định suy từ
    /// tuỳ chọn kênh toàn cục. Giúp FE hiển thị đúng trạng thái "kế thừa" thay vì "đã đặt".
    /// </summary>
    public bool IsCustomized { get; set; }
}

/// <summary>Sprint 6.3 NOTI3-04 (#704) — toàn bộ ma trận của một người dùng.</summary>
public class NotificationPreferenceMatrixDto
{
    /// <summary>Tuỳ chọn cấp kênh (công tắc lớn) — vẫn thắng mọi dòng nhóm.</summary>
    public NotificationPreferenceDto Channels { get; set; } = new();

    /// <summary>Đủ 6 nhóm, kể cả nhóm chưa tuỳ chỉnh (khi đó <c>IsCustomized=false</c>).</summary>
    public List<NotificationCategoryPreferenceDto> Categories { get; set; } = new();
}

public class NotificationPreferenceMatrixResponse : CommonResponse<NotificationPreferenceMatrixDto> { }
