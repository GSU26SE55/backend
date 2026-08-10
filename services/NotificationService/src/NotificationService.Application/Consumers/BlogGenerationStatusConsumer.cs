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

            // KHÔNG dán ErrorMessage (message exception .NET) vào Body: người đọc là Manager/Admin
            // chứ không phải người debug, và chuỗi kiểu "The request was canceled due to the
            // configured HttpClient.Timeout of 60 seconds elapsing." vừa không nói họ phải làm gì
            // vừa lộ chi tiết nội bộ. Diễn giải sang nguyên nhân + hành động tiếp theo;
            // message gốc đẩy xuống PayloadJson cho ai cần tra.
            var body = isSuccess
                ? $"Bài blog \"{Truncate(evt.BlogTitle)}\" đã được AI tạo thành công và đang ở trạng thái Nháp. Hãy review và publish khi sẵn sàng."
                : $"Bài blog \"{Truncate(evt.BlogTitle)}\" chưa tạo được: {DescribeFailure(evt.ErrorMessage)}";

            var cmd = new CreateNotificationCommand
            {
                UserId = evt.RequestedByUserId,
                Type = notificationType,
                Channel = NotificationChannelEnum.InApp,
                Title = title,
                Body = body,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    blogPostId = evt.BlogPostId,
                    // Giữ nguyên message kỹ thuật ở đây — FE hiện trong mục "Dữ liệu kèm theo"
                    // (gập lại), ai cần tra vẫn có, người dùng thường không phải đọc.
                    errorMessage = isSuccess ? null : evt.ErrorMessage,
                }),
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

    /// <summary>
    /// Quy message lỗi kỹ thuật về một câu người dùng hiểu được, kèm việc họ có thể làm tiếp.
    ///
    /// Nhận diện theo từ khoá vì lỗi đến từ nhiều nguồn (HttpClient, AI provider, chính service)
    /// và không có mã lỗi chung; không khớp mẫu nào thì trả câu chung chứ KHÔNG rơi về việc
    /// đọc nguyên message gốc.
    /// </summary>
    private static string DescribeFailure(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return "Lỗi không xác định. Bạn hãy thử tạo lại.";

        var e = errorMessage.ToLowerInvariant();

        if (e.Contains("timeout") || e.Contains("canceled") || e.Contains("cancelled"))
            return "AI xử lý quá lâu nên đã dừng. Bạn hãy thử tạo lại, hoặc rút ngắn bài gốc nếu nội dung quá dài.";

        if (e.Contains("429") || e.Contains("rate limit") || e.Contains("quota"))
            return "Dịch vụ AI đang quá tải. Bạn hãy thử lại sau ít phút.";

        if (e.Contains("unauthorized") || e.Contains("401") || e.Contains("api key") || e.Contains("forbidden") || e.Contains("403"))
            return "Kết nối tới dịch vụ AI bị từ chối. Việc này cần quản trị viên kiểm tra cấu hình.";

        if (e.Contains("connection") || e.Contains("socket") || e.Contains("host") || e.Contains("network"))
            return "Không kết nối được tới dịch vụ AI. Bạn hãy thử lại sau ít phút.";

        return "Dịch vụ AI gặp sự cố khi xử lý. Bạn hãy thử tạo lại.";
    }

    private static string Truncate(string? text, int max = 100)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length > max ? text[..max] + "..." : text;
    }
}
