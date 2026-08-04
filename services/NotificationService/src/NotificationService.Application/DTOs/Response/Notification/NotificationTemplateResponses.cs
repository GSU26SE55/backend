using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>Kết quả dựng thử template — KHÔNG gửi đi đâu cả.</summary>
public class NotificationTemplatePreviewDto
{
    /// <summary>Giá trị số của <c>NotificationTypeEnum</c>.</summary>
    public NotificationTypeEnum Type { get; set; }

    /// <summary>Giá trị số của <c>NotificationChannelEnum</c>.</summary>
    public NotificationChannelEnum Channel { get; set; }

    public int Version { get; set; }

    /// <summary>Tiêu đề sau khi render.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Nội dung sau khi render.</summary>
    public string Body { get; set; } = string.Empty;
}

/// <summary>Kết quả gửi thử — địa chỉ nhận LUÔN là admin đang đăng nhập (R-46).</summary>
public class NotificationTemplateTestSendDto
{
    /// <summary>Số lượt gửi thử còn lại trong giờ hiện tại.</summary>
    public int RemainingThisHour { get; set; }
}

/// <summary>Một trang template (danh sách quản trị).</summary>
public class NotificationTemplateListResponse : CommonResponse<PaginationResponse<NotificationTemplateDto>> { }

/// <summary>Chi tiết một template.</summary>
public class NotificationTemplateResponse : CommonResponse<NotificationTemplateDto> { }

/// <summary>
/// Kết quả một hành động thay đổi trạng thái (tạo / sửa / quay lui / xoá).
/// <c>Data</c> là Id của bản ghi VỪA có hiệu lực — với tạo/sửa là bản mới, với quay lui là bản được
/// bật, với xoá là bản bị xoá. FE dùng nó để chọn đúng dòng sau khi làm mới danh sách.
/// </summary>
public class NotificationTemplateActionResponse : CommonResponse<Guid> { }

/// <summary>Kết quả dựng thử.</summary>
public class NotificationTemplatePreviewResponse : CommonResponse<NotificationTemplatePreviewDto> { }

/// <summary>Kết quả gửi thử.</summary>
public class NotificationTemplateTestSendResponse : CommonResponse<NotificationTemplateTestSendDto> { }
