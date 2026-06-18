using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi NotificationService muốn gửi email notification chung (ticket alert, SLA breach, ...).
/// EmailService consume và gửi email thực tế — template rendering xử lý ở #111.
/// </summary>
public record SendNotificationEmailEvent(
    Guid NotificationId,
    string ToEmail,
    string Subject,
    string Body,
    string SourceService = "notification"
) : IntegrationEvent;
