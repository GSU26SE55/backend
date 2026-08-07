using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class GetNotificationByIdQueryHandler
    : IRequestHandler<GetNotificationByIdQuery, NotificationResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetNotificationByIdQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationResponse> Handle(
        GetNotificationByIdQuery request, CancellationToken cancellationToken)
    {
        // Lọc UserId ngay trong câu truy vấn (không load rồi mới so) — cùng khuôn với
        // MarkNotificationOpenedCommandHandler.
        //
        // KHÔNG lọc theo Channel ở đây: danh sách mặc định chỉ trả feed InApp, nhưng khi đã cầm
        // đúng id thì đó là yêu cầu xem một bản ghi cụ thể. Chặn theo channel sẽ làm record giao
        // nhận (Push/Email/Sms) trả 404 dù vẫn là của chính chủ.
        var dto = await _unitOfWork.Notifications.GetAllAsync()
            .AsNoTracking()
            .Where(n => n.Id == request.Id && n.UserId == request.UserId && !n.IsDeleted)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                UserId = n.UserId,
                Type = n.Type,
                Channel = n.Channel,
                Status = n.Status,
                Title = n.Title,
                Body = n.Body,
                PayloadJson = n.PayloadJson,
                EntityType = n.EntityType,
                EntityId = n.EntityId,
                SentAt = n.SentAt,
                ReadAt = n.ReadAt,
                CreatedAt = n.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Không tồn tại HOẶC của người khác → 404 (không leak existence, tránh IDOR).
        if (dto is null)
        {
            return new NotificationResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy notification."
            };
        }

        return new NotificationResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto
        };
    }
}
