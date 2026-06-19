using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

public interface INotificationDispatcher
{
    Task DispatchAsync(DispatchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Input cho Dispatcher — 1 event → nhiều recipients → nhiều channels.
/// </summary>
public class DispatchRequest
{
    public NotificationTypeEnum Type { get; set; }
    public List<RecipientInfo> Recipients { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }

    /// <summary>
    /// True → bỏ qua quiet hours (smoke/water/critical alert bypass per overall.md §3.4).
    /// Dispatcher cũng tự bypass nếu Type thuộc <c>CriticalTypes</c>.
    /// </summary>
    public bool BypassQuietHours { get; set; }

    public Guid? EntityId { get; set; }
    public string? EntityType { get; set; }
}

public class RecipientInfo
{
    public Guid UserId { get; set; }

    /// <summary>Null → email channel bị skip.</summary>
    public string? Email { get; set; }

    /// <summary>Null → SMS channel bị skip.</summary>
    public string? PhoneNumber { get; set; }
}
