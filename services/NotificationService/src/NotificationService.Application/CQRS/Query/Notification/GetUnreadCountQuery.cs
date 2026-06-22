using MediatR;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// Đếm số notification chưa đọc (Status != Read &amp;&amp; !IsDeleted) của user hiện tại — phục vụ badge.
/// </summary>
public class GetUnreadCountQuery : IRequest<NotificationCountResponse>
{
    /// <summary>Set từ JWT claim trong controller.</summary>
    public Guid UserId { get; set; }
}
