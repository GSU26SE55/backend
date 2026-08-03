using MediatR;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using SharedInfrastructure.Extensions;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateGetListQueryHandler
    : IRequestHandler<NotificationTemplateGetListQuery, NotificationTemplateListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationTemplateGetListQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationTemplateListResponse> Handle(
        NotificationTemplateGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.NotificationTemplates.GetAllAsync(false).Where(t => !t.IsDeleted);

        if (request.Type.HasValue)
            query = query.Where(t => t.Type == request.Type.Value);
        if (request.Channel.HasValue)
            query = query.Where(t => t.Channel == request.Channel.Value);
        if (request.ActiveOnly == true)
            query = query.Where(t => t.IsActive);

        var page = await query
            .OrderBy(t => t.Type).ThenBy(t => t.Channel).ThenByDescending(t => t.Version)
            // Chốt chặn cuối: 2 bản cùng (type, channel, version) là không thể (đã có unique index),
            // nhưng thứ tự KHÔNG toàn phần thì Postgres được phép trả khác nhau giữa các lần chạy —
            // khi đó một dòng có thể xuất hiện ở 2 trang, hoặc biến mất hẳn.
            .ThenBy(t => t.Id)
            .Select(t => new NotificationTemplateDto
            {
                Id = t.Id,
                Type = t.Type,
                Channel = t.Channel,
                Version = t.Version,
                IsActive = t.IsActive,
                TitleTemplate = t.TitleTemplate,
                BodyTemplate = t.BodyTemplate,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new NotificationTemplateListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page,
        };
    }
}
