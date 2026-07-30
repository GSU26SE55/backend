using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-02 (#673) — consumer còn thiếu cho <see cref="SendNotificationEmailEvent"/>.
///
/// <c>NotificationService.EmailBusChannel</c> publish event này cho MỌI email của notification
/// pipeline (SLA breach, battery escalation, environmental incident, saga failed, chat mention…),
/// nhưng EmailService chỉ có 5 consumer OTP/invite — không consumer nào cho event này. RabbitMQ
/// drop message không có binding: không lỗi, không log, email biến mất
/// (reviewnotification.md §3.2). Kể cả sau khi bật dispatcher (NOTI-01) thì thiếu bước này email
/// vẫn không tới hộp thư.
///
/// Dedup qua Redis inbox theo <c>(consumerName, messageId)</c> — cùng pattern 4 consumer OTP.
/// </summary>
public class SendNotificationEmailConsumer : IConsumer<SendNotificationEmailEvent>
{
    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendNotificationEmailConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendNotificationEmailConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendNotificationEmailConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendNotificationEmailEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendNotificationEmailConsumer), async () =>
        {
            if (string.IsNullOrWhiteSpace(msg.ToEmail))
            {
                _logger.LogWarning(
                    "SendNotificationEmail {NotificationId}: thiếu địa chỉ nhận — bỏ qua.", msg.NotificationId);
                return;
            }

            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";
                var subject = string.IsNullOrWhiteSpace(msg.Subject) ? appName : msg.Subject;

                var htmlBody = await BuildHtmlBodyAsync(msg, appName, subject, context.CancellationToken);

                // Sprint 6.3 NOTI3-15 (#715) — hủy đăng ký một chạm (RFC 8058).
                // Gmail/Yahoo yêu cầu với người gửi số lượng lớn từ 2024: không có nút hủy thì người
                // nhận sẽ bấm "báo cáo spam", và tỷ lệ spam > 0.3% là mất reputation domain.
                // Email giao dịch (OTP, đặt lại mật khẩu) đi consumer khác và cố ý KHÔNG có header này.
                var headers = BuildUnsubscribeHeaders(msg.UnsubscribeUrl);

                await _emailSender.SendAsync(msg.ToEmail, subject, htmlBody, headers, context.CancellationToken);

                _logger.LogInformation(
                    "Notification email sent to {Email} (notificationId={NotificationId}, source={Source}).",
                    msg.ToEmail, msg.NotificationId, msg.SourceService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send notification email to {Email} (notificationId={NotificationId}).",
                    msg.ToEmail, msg.NotificationId);
                throw;   // để MassTransit retry
            }
        });
    }

    /// <summary>
    /// Body của notification có hai dạng:
    /// <list type="bullet">
    /// <item>HTML dựng sẵn — một số consumer (battery escalation, environmental incident, saga failed)
    /// render template Handlebars rồi nhét thẳng vào Body. Gửi nguyên, KHÔNG bọc lại template khác
    /// vì <see cref="EmailTemplateRenderer"/> HTML-encode mọi placeholder nên sẽ hiện ra thẻ thô.</item>
    /// <item>Text thuần — đa số consumer. Bọc vào <c>NotificationGeneric.html</c>; placeholder được
    /// HTML-encode nên nội dung do người dùng nhập (tin nhắn chat…) không thể chèn HTML.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// Sprint 6.3 NOTI3-15 (#715) — cặp header hủy một chạm.
    ///
    /// <c>List-Unsubscribe-Post</c> là thứ biến link thành **một chạm**: thiếu nó, Gmail chỉ mở
    /// trang web và người dùng phải tự thao tác tiếp — không đạt yêu cầu của quy định 2024.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildUnsubscribeHeaders(string? unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(unsubscribeUrl))
            return null;

        return new Dictionary<string, string>
        {
            ["List-Unsubscribe"] = $"<{unsubscribeUrl}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
        };
    }

    private async Task<string> BuildHtmlBodyAsync(
        SendNotificationEmailEvent msg, string appName, string subject, CancellationToken ct)
    {
        var body = msg.Body ?? string.Empty;

        if (LooksLikeHtml(body))
            return body;

        var values = new Dictionary<string, string?>
        {
            ["AppName"] = appName,
            ["Subject"] = subject,
            ["Body"] = body,
        };

        try
        {
            return await _templateRenderer.RenderAsync(EmailTemplates.NotificationGeneric, values, ct);
        }
        catch (FileNotFoundException)
        {
            // Không có template trên disk vẫn phải gửi được — thà email trơ còn hơn mất email.
            _logger.LogWarning(
                "Template {Template} không tồn tại — gửi notification email dạng tối giản.",
                EmailTemplates.NotificationGeneric);
            return $"<p>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(body)}</p>";
        }
    }

    private static bool LooksLikeHtml(string body) =>
        body.TrimStart().StartsWith('<');
}
