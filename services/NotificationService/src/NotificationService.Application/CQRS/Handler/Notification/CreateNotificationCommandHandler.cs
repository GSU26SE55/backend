using System.Text.Json;
using System.Text.Json.Nodes;
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
        // Sprint IoT-2 #IoT2-31 — merge BypassQuietHours flag vào PayloadJson để dispatcher (Sprint 6+)
        // có thể đọc và bỏ qua NotificationPreference.QuietHoursStart/End khi gửi qua transport.
        var payloadJson = request.PayloadJson;
        if (request.BypassQuietHours)
        {
            try
            {
                var node = string.IsNullOrWhiteSpace(payloadJson)
                    ? new JsonObject()
                    : JsonNode.Parse(payloadJson) as JsonObject ?? new JsonObject();
                node["bypassQuietHours"] = true;
                payloadJson = node.ToJsonString();
            }
            catch (JsonException)
            {
                // Payload không phải JSON object → wrap.
                payloadJson = JsonSerializer.Serialize(new { bypassQuietHours = true, original = payloadJson });
            }
        }

        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type,
            Channel = request.Channel,
            Status = NotificationStatusEnum.Pending,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            PayloadJson = payloadJson,
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
