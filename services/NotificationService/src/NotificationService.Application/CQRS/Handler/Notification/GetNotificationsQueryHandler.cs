using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

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

        // Default InApp-only so Push records (used only for device delivery) don't appear in the list.
        var effectiveChannel = request.Channel ?? NotificationChannelEnum.InApp;
        query = query.Where(n => n.Channel == effectiveChannel);

        if (request.Status.HasValue)
            query = query.Where(n => n.Status == request.Status.Value);

        if (request.UnreadOnly == true)
            query = query.Where(n => n.Status != NotificationStatusEnum.Read);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(cancellationToken);

        return new NotificationListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<NotificationDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
