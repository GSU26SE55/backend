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
        // Sprint 6.3 NOTI3-01 (#701) — chỉ đếm record feed (InApp). Trước đó đếm cả record
        // giao nhận Push/Email/Sms nên badge phồng 2–4 lần so với số dòng user thực sự nhìn thấy.
        var count = await _unitOfWork.Notifications.GetAllAsync(false)
            .Where(n => n.UserId == request.UserId
                        && !n.IsDeleted
                        && n.Channel == NotificationChannelEnum.InApp
                        // Sprint 6.3 NOTI3-14 (#714) — Opened mạnh hơn Read (user bấm hẳn vào
                        // notification), nên cũng phải trừ khỏi badge. Thiếu vế này thì noti đã mở
                        // vẫn hiện chưa đọc.
                        && n.Status != NotificationStatusEnum.Read
                        && n.Status != NotificationStatusEnum.Opened)
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
