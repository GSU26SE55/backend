using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class GetUnreadCountQueryHandler : IRequestHandler<GetUnreadCountQuery, NotificationCountResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetUnreadCountQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationCountResponse> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        // Read-only → no-tracking.
        var count = await _unitOfWork.Notifications.GetAllAsync(false)
            .Where(n => n.UserId == request.UserId && !n.IsDeleted && n.Status != NotificationStatusEnum.Read)
            .CountAsync(cancellationToken);

        return new NotificationCountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Lấy số notification chưa đọc thành công.",
            Data = count
        };
    }
}
