using MediatR;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Application.CQRS.Query.NotificationTemplate;

/// <summary>
/// Tra danh sách biến hợp lệ theo từng loại thông báo — để trình soạn template gợi ý đúng tên biến
/// thay vì để người soạn tự đoán. Tự đoán chính là cách <c>{{ticketCode}}</c> ra đời trong khi
/// consumer ghi khoá <c>code</c>.
/// </summary>
public class NotificationTemplateVariableListQuery : IRequest<NotificationTemplateVariableListResponse>
{
}

/// <summary>
/// Bảng độ phủ: cặp (loại × kênh) nào đang sinh thông báo thật mà chưa có template, và template
/// nào đang dùng biến không tồn tại.
/// </summary>
public class NotificationTemplateCoverageQuery : IRequest<NotificationTemplateCoverageResponse>
{
}
