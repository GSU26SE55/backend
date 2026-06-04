using MediatR;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class CreateNotificationCommandHandler : IRequestHandler<CreateNotificationCommand, NotificationActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public CreateNotificationCommandHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationActionResponse> Handle(CreateNotificationCommand request, CancellationToken cancellationToken)
    {
        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type,
            Channel = request.Channel,
            Status = NotificationStatusEnum.Pending,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            PayloadJson = request.PayloadJson,
            EntityType = request.EntityType?.Trim(),
            EntityId = request.EntityId
        };

        await _unitOfWork.Notifications.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new NotificationActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo notification thành công.",
            Data = entity.Id
        };
    }
}
