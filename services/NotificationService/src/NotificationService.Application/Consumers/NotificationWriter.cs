using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Helper ghi Notification record TRỰC TIẾP qua <see cref="INotificationUnitOfWork"/>,
/// bỏ qua <c>CreateNotificationCommand</c>/ValidationBehavior (vốn reject recipient placeholder
/// <see cref="System.Guid.Empty"/>). Consumer tạo notification cho recipient chưa-resolve (broadcast,
/// dispatcher fan-out Sprint 6) hoặc recipient thật (StaffId/CustomerId/AccountId) đều dùng helper này.
///
/// Lỗi DB sẽ propagate → MassTransit auto-retry (KHÔNG nuốt exception).
/// </summary>
internal static class NotificationWriter
{
    /// <summary>Channel mặc định cho notification hướng user (in-app list + push device).</summary>
    public static readonly NotificationChannelEnum[] InAppPush =
    {
        NotificationChannelEnum.InApp,
        NotificationChannelEnum.Push
    };

    /// <summary>Chỉ in-app (vd welcome, không cần push).</summary>
    public static readonly NotificationChannelEnum[] InAppOnly =
    {
        NotificationChannelEnum.InApp
    };

    public static async Task WriteAsync(
        INotificationUnitOfWork unitOfWork,
        IReadOnlyCollection<Guid> recipientIds,
        NotificationTypeEnum type,
        IReadOnlyCollection<NotificationChannelEnum> channels,
        string title,
        string body,
        string? payloadJson,
        string entityType,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        foreach (var userId in recipientIds)
        {
            foreach (var channel in channels)
            {
                await unitOfWork.Notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = type,
                    Channel = channel,
                    Status = NotificationStatusEnum.Pending,
                    Title = title,
                    Body = body,
                    PayloadJson = payloadJson,
                    EntityType = entityType,
                    EntityId = entityId
                });
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
