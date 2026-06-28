using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, NotificationActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationActionResponse> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        // Tracking ON — sẽ update entity. Chỉ lấy noti thuộc về chính user (ownership).
        var entity = await _unitOfWork.Notifications.GetAllAsync()
            .FirstOrDefaultAsync(
                n => n.Id == request.Id && n.UserId == request.UserId && !n.IsDeleted,
                cancellationToken);

        // Không tồn tại HOẶC của người khác → 404 (không leak existence, tránh IDOR).
        if (entity is null)
        {
            return new NotificationActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy notification."
            };
        }

        // Idempotent: đã Read rồi → vẫn 200, không ghi lại.
        if (entity.Status == NotificationStatusEnum.Read)
        {
            return new NotificationActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Notification đã được đánh dấu đã đọc.",
                Data = entity.Id
            };
        }

        entity.Status = NotificationStatusEnum.Read;
        entity.ReadAt = DateTime.UtcNow;
        _unitOfWork.Notifications.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new NotificationActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đánh dấu đã đọc thành công.",
            Data = entity.Id
        };
    }
}
