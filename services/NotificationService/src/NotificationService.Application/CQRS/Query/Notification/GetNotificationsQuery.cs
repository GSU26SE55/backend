using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Requests;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// Lấy danh sách notification của 1 user (mặc định là user hiện tại — set từ controller).
/// Hỗ trợ filter theo Type, Channel, Status.
/// </summary>
public class GetNotificationsQuery : PaginationRequest, IRequest<NotificationListResponse>
{
    public Guid UserId { get; set; }
    public NotificationTypeEnum? Type { get; set; }
    public NotificationChannelEnum? Channel { get; set; }
    public NotificationStatusEnum? Status { get; set; }

    /// <summary>Chỉ lấy notification chưa đọc (Status != Read).</summary>
    public bool? UnreadOnly { get; set; }
}
