using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedInfrastructure.Extensions;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, NotificationListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetNotificationsQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationListResponse> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Notifications.GetAllAsync()
            .Where(n => !n.IsDeleted && n.UserId == request.UserId)
            .AsNoTracking();

        if (request.Type.HasValue)
            query = query.Where(n => n.Type == request.Type.Value);

        // Sprint 6.3 NOTI3-01 (#701) — feed = 1 dòng / sự kiện.
        // Record của các channel khác (Push/Email/Sms) là bản ghi GIAO NHẬN, không phải mục hiển thị;
        // trả hết ra sẽ khiến user thấy cùng một thông báo lặp 2–4 lần.
        if (request.Channel.HasValue)
            query = query.Where(n => n.Channel == request.Channel.Value);
        else if (!request.IncludeAllChannels)
            query = query.Where(n => n.Channel == NotificationChannelEnum.InApp);

        if (request.Status.HasValue)
            query = query.Where(n => n.Status == request.Status.Value);

        if (request.UnreadOnly == true)
        {
            // Sprint 6.3 NOTI3-14 (#714) — Opened cũng là "đã xem", không được trả về ở filter chưa đọc.
            query = query.Where(n => n.Status != NotificationStatusEnum.Read
                                     && n.Status != NotificationStatusEnum.Opened);
        }

        var page = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id) // tie-breaker cố định — pagination ổn định
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
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new NotificationListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
