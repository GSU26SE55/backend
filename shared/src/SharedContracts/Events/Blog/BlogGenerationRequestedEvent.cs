using SharedContracts.Events.Root;

namespace SharedContracts.Events.Blog;

/// <summary>
/// Publish khi Manager/Admin yêu cầu AI tạo blog từ KB article.
/// Subscriber: TicketService BlogGenerationConsumer — gọi DeepSeek dưới nền.
/// </summary>
public record BlogGenerationRequestedEvent(
    Guid BlogPostId,
    Guid KbArticleId,
    Guid RequestedByUserId
) : IntegrationEvent;
