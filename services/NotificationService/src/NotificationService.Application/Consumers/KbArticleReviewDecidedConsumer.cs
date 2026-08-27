using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events.KnowledgeBase;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="KbArticleReviewDecidedEvent"/>: báo cho NGƯỜI ĐỀ XUẤT biết bản sửa
/// KB của họ được duyệt hay bị từ chối.
///
/// Người nhận là <c>SubmittedByUserId</c> chứ không phải người bấm duyệt — publisher đã đọc giá
/// trị này TRƯỚC khi xoá <c>PendingReviewBy</c>, vì sau khi xoá thì không còn đường nào truy ra
/// ai là người gửi. Nhánh từ chối luôn kèm lý do: không có nó thì người sửa biết bài bị trả về
/// nhưng không biết phải sửa gì.
/// </summary>
public class KbArticleReviewDecidedConsumer : IConsumer<KbArticleReviewDecidedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<KbArticleReviewDecidedConsumer> _logger;

    public KbArticleReviewDecidedConsumer(
        IMediator mediator,
        IInboxStore inboxStore,
        ILogger<KbArticleReviewDecidedConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<KbArticleReviewDecidedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(KbArticleReviewDecidedConsumer), async () =>
        {
            var evt = context.Message;

            if (evt.SubmittedByUserId == Guid.Empty)
            {
                _logger.LogWarning(
                    "KbArticleReviewDecided without a submitter ArticleId={ArticleId} — skip.",
                    evt.ArticleId);
                return;
            }

            var reviewer = string.IsNullOrWhiteSpace(evt.DecidedByName) ? "A reviewer" : evt.DecidedByName;

            var title = evt.Approved
                ? "Your guide article change was approved"
                : "Your guide article change was rejected";

            // Lý do từ chối là bắt buộc ở API (RejectReviewCommand.ValidateAsync), nhưng vẫn phòng
            // trường hợp rỗng thay vì in ra "Reason: ." cụt lủn.
            var reason = string.IsNullOrWhiteSpace(evt.RejectReason)
                ? "No reason was provided."
                : $"Reason: {Truncate(evt.RejectReason, 200)}";

            var body = evt.Approved
                ? $"{reviewer} approved your change to the article \"{Truncate(evt.ArticleTitle)}\". It is now live."
                : $"{reviewer} rejected your change to the article \"{Truncate(evt.ArticleTitle)}\". {reason}";

            var payload = JsonSerializer.Serialize(new
            {
                articleId = evt.ArticleId,
                articleTitle = evt.ArticleTitle,
                decidedByUserId = evt.DecidedByUserId,
                decidedByName = evt.DecidedByName,
                approved = evt.Approved,
                rejectReason = evt.Approved ? null : evt.RejectReason,
            });

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.SubmittedByUserId,
                Type = evt.Approved
                    ? NotificationTypeEnum.KbArticleReviewApproved
                    : NotificationTypeEnum.KbArticleReviewRejected,
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
                    "Failed to create KbArticleReviewDecided notification ArticleId={ArticleId} UserId={UserId}: {Message}",
                    evt.ArticleId, evt.SubmittedByUserId, result.Message);
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
