using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Blog;
using SharedInfrastructure.Idempotency;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Consumer cho <see cref="BlogGenerationStatusChangedEvent"/>:
/// Gửi InApp notification cho user đã yêu cầu generate blog (Manager/Admin)
/// khi AI hoàn thành (Draft) hoặc thất bại (GenerationFailed).
/// </summary>
public class BlogGenerationStatusConsumer : IConsumer<BlogGenerationStatusChangedEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<BlogGenerationStatusConsumer> _logger;

    public BlogGenerationStatusConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<BlogGenerationStatusConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BlogGenerationStatusChangedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(BlogGenerationStatusConsumer), async () =>
        {
            var evt = context.Message;

            var isSuccess = evt.Status == "Draft";
            var notificationType = isSuccess ? NotificationTypeEnum.BlogGenerationCompleted : NotificationTypeEnum.BlogGenerationFailed;

            var title = isSuccess
                ? "Blog đã được tạo thành công"
                : "Tạo blog bằng AI thất bại";

            var body = isSuccess
                ? $"Bài blog \"{Truncate(evt.BlogTitle)}\" đã được AI tạo thành công và đang ở trạng thái Nháp. Hãy review và publish khi sẵn sàng."
                : $"Tạo blog \"{Truncate(evt.BlogTitle)}\" thất bại: {Truncate(evt.ErrorMessage ?? "Lỗi không xác định")}.";

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.RequestedByUserId,
                Type = notificationType,
                Channel = NotificationChannelEnum.InApp,
                Title = title,
                Body = body,
                PayloadJson = JsonSerializer.Serialize(new { blogPostId = evt.BlogPostId }),
                EntityType = "BlogPost",
                EntityId = evt.BlogPostId,
            };

            var result = await _mediator.Send(cmd, context.CancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to create BlogGenerationStatus notification for BlogPostId={BlogPostId}: {Message}",
                    evt.BlogPostId, result.Message);
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
