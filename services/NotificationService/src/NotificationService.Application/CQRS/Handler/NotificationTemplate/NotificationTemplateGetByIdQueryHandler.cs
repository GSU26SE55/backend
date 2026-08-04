using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateGetByIdQueryHandler
    : IRequestHandler<NotificationTemplateGetByIdQuery, NotificationTemplateResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationTemplateGetByIdQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationTemplateResponse> Handle(
        NotificationTemplateGetByIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .Where(t => t.Id == request.Id && !t.IsDeleted)
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
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return new NotificationTemplateResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy template.",
            };
        }

        return new NotificationTemplateResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto,
        };
    }
}
