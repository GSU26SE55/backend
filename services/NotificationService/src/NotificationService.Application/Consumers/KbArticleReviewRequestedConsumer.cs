using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events.KnowledgeBase;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="KbArticleReviewRequestedEvent"/>: báo Manager/Admin có bài KB đang
/// chờ duyệt.
///
/// Trước đây luồng duyệt KB im lặng hoàn toàn — người có quyền approve/reject không nhận được
/// gì, chỉ có badge "chờ duyệt" ở sidebar (cache 60s, không poll) làm manh mối. Hệ quả là bản
/// sửa nằm chờ cho tới khi tình cờ có ai mở màn hình KB ra xem.
/// </summary>
public class KbArticleReviewRequestedConsumer : IConsumer<KbArticleReviewRequestedEvent>
{
    private readonly IMediator _mediator;
    private readonly IRecipientResolver _recipientResolver;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<KbArticleReviewRequestedConsumer> _logger;

    public KbArticleReviewRequestedConsumer(
        IMediator mediator,
        IRecipientResolver recipientResolver,
        IInboxStore inboxStore,
        ILogger<KbArticleReviewRequestedConsumer> logger)
    {
        _mediator = mediator;
        _recipientResolver = recipientResolver;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<KbArticleReviewRequestedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(KbArticleReviewRequestedConsumer), async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(
                context.CancellationToken, "Manager", "Admin");

            if (recipientIds.Count == 0)
            {
                _logger.LogWarning(
                    "No Manager/Admin recipient resolved for KbArticleReviewRequested ArticleId={ArticleId} — skip.",
                    evt.ArticleId);
                return;
            }

            var author = string.IsNullOrWhiteSpace(evt.RequestedByName) ? "A staff member" : evt.RequestedByName;

            var title = evt.IsNewArticle
                ? "New guide article awaiting approval"
                : "Guide article change awaiting approval";

            // Mô tả thay đổi do người sửa tự nhập nên có thể rỗng hoặc rất dài — chỉ ghép vào khi
            // thực sự có nội dung, và cắt ngắn để một dòng mô tả dài không nuốt cả thông báo.
            var change = string.IsNullOrWhiteSpace(evt.ChangeDescription)
                ? string.Empty
                : $" Change note: {Truncate(evt.ChangeDescription, 160)}";

            var body = evt.IsNewArticle
                ? $"{author} created the article \"{Truncate(evt.ArticleTitle)}\" and submitted it for approval.{change}"
                : $"{author} edited the article \"{Truncate(evt.ArticleTitle)}\" and submitted the change for approval.{change}";

            var payload = JsonSerializer.Serialize(new
            {
                articleId = evt.ArticleId,
                articleTitle = evt.ArticleTitle,
                requestedByUserId = evt.RequestedByUserId,
                requestedByName = evt.RequestedByName,
                changeDescription = evt.ChangeDescription,
                isNewArticle = evt.IsNewArticle,
            });

            foreach (var userId in recipientIds)
            {
                // Người tự gửi duyệt không cần nhận thông báo của chính mình — trường hợp này xảy
                // ra khi một Admin/Manager tạo bài mới (bài mới luôn vào PendingReview kể cả khi
                // người tạo có quyền duyệt).
                if (userId == evt.RequestedByUserId)
                    continue;

                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.KbArticleReviewRequested,
                    Channel = NotificationChannelEnum.InApp,
                    Title = title,
                    Body = body,
                    PayloadJson = payload,
                    EntityType = "KnowledgeArticle",
                    EntityId = evt.ArticleId,
                };

                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create KbArticleReviewRequested notification ArticleId={ArticleId} UserId={UserId}: {Message}",
                        evt.ArticleId, userId, result.Message);
                }
            }
        });
    }

    private static string Truncate(string? text, int max = 100)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length > max ? text[..max] + "..." : text;
    }
}
