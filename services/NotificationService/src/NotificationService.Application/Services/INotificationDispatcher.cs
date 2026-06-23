using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

public interface INotificationDispatcher
{
    Task DispatchAsync(DispatchRequest request, CancellationToken ct = default);
}

public class RecipientInfo
{
    public Guid UserId { get; set; }

    /// <summary>Null → email channel bị skip.</summary>
    public string? Email { get; set; }

    /// <summary>Null → SMS channel bị skip.</summary>
    public string? PhoneNumber { get; set; }
}
