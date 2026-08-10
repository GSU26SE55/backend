using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>
/// Biến dùng được cho một loại thông báo — nguồn cho ô gợi ý trên trình soạn template.
/// </summary>
public class NotificationTemplateVariableGroupDto
{
    public NotificationTypeEnum Type { get; set; }

    /// <summary>Tên enum, để FE khỏi phải giữ bảng ánh xạ số→tên riêng.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Sáu biến luôn có, không phụ thuộc loại thông báo: <c>Title</c>, <c>Body</c>,
    /// <c>EntityType</c>, <c>EntityId</c>, <c>UserId</c>, <c>CreatedAt</c>.
    /// </summary>
    public List<string> Builtin { get; set; } = new();

    /// <summary>
    /// Khoá riêng của loại này, lấy từ payload mà consumer thật sự ghi. Rỗng nghĩa là consumer
    /// không ghi payload — template chỉ được dùng <see cref="Builtin"/>.
    /// </summary>
    public List<string> Payload { get; set; } = new();
}

/// <summary>
/// Một ô của bảng độ phủ: cặp (loại × kênh) đã từng sinh thông báo thật, kèm việc nó có template
/// đang hoạt động hay không.
/// </summary>
public class NotificationTemplateCoverageDto
{
    public NotificationTypeEnum Type { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public NotificationChannelEnum Channel { get; set; }

    /// <summary>Số dòng thông báo đã sinh cho cặp này — thước đo mức độ đáng quan tâm.</summary>
    public int NotificationCount { get; set; }

    /// <summary>
    /// <c>false</c> ⇒ mọi thông báo của cặp này đang dùng chuỗi hardcode trong consumer; sửa câu
    /// chữ bắt buộc phải sửa code và deploy lại.
    /// </summary>
    public bool HasActiveTemplate { get; set; }

    /// <summary>
    /// Biến mà template đang dùng nhưng không có trong dữ liệu của loại này ⇒ render ra rỗng.
    /// Rỗng là tốt. Chỉ có giá trị khi <see cref="HasActiveTemplate"/> là <c>true</c>.
    /// </summary>
    public List<string> UnknownVariables { get; set; } = new();
}

/// <summary>Danh sách biến hợp lệ theo từng loại thông báo.</summary>
public class NotificationTemplateVariableListResponse
    : CommonResponse<List<NotificationTemplateVariableGroupDto>>
{ }

/// <summary>Bảng độ phủ template so với thông báo thật đã sinh.</summary>
public class NotificationTemplateCoverageResponse
    : CommonResponse<List<NotificationTemplateCoverageDto>>
{ }
