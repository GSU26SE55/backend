using MediatR;
using NotificationService.Application.DTOs.Response.Setting;

namespace NotificationService.Application.CQRS.Query.Setting;

/// <summary>
/// Đọc đường vận chuyển push đang áp dụng cho toàn hệ thống (ADR-0019).
/// Không có tham số — đây là cấu hình đơn lẻ, không phải danh sách.
/// </summary>
public class GetPushTransportQuery : IRequest<PushTransportResponse>
{
}
