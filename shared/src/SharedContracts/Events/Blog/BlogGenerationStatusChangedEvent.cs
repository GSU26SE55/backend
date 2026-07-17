using SharedContracts.Events.Root;

namespace SharedContracts.Events.Blog;

/// <summary>
/// Publish khi BlogGenerationConsumer hoàn thành (thành công hoặc thất bại).
/// Subscriber: NotificationService BlogGenerationStatusConsumer — InApp notification.
/// </summary>
public record BlogGenerationStatusChangedEvent(
    Guid BlogPostId,
    Guid RequestedByUserId,
    string Status,       // "Draft" | "GenerationFailed"
    string BlogTitle,
    string? ErrorMessage
) : IntegrationEvent;
