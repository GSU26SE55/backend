using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand, NotificationCountResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public MarkAllNotificationsReadCommandHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationCountResponse> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        // Tracking ON — sẽ update. Lấy mọi noti chưa đọc của chính user.
        var unread = await _unitOfWork.Notifications.GetAllAsync()
            // Sprint 6.3 NOTI3-14 (#714) — bỏ qua cả Opened: hạ Opened xuống Read là mất thông tin
            // (Opened = user thực sự bấm mở, Read = chỉ lướt qua feed).
            .Where(n => n.UserId == request.UserId
                        && !n.IsDeleted
                        && n.Status != NotificationStatusEnum.Read
                        && n.Status != NotificationStatusEnum.Opened)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
        {
            return new NotificationCountResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Không có notification chưa đọc.",
                Data = 0
            };
        }

        var now = DateTime.UtcNow;
        foreach (var entity in unread)
        {
            entity.Status = NotificationStatusEnum.Read;
            entity.ReadAt = now;
            _unitOfWork.Notifications.UpdateAsync(entity);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new NotificationCountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đánh dấu tất cả đã đọc thành công.",
            Data = unread.Count
        };
    }
}
