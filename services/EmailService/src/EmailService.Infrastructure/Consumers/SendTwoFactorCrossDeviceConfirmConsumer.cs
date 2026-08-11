using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

/// <summary>
/// GH-768 — gửi email chứa link xác nhận bật 2FA xuyên thiết bị.
/// </summary>
/// <remarks>
/// AuthService publish <see cref="SendTwoFactorCrossDeviceConfirmEmailEvent"/> từ #AUTH-51 và
/// endpoint trả 200, nhưng EmailService chưa từng đăng ký consumer nào cho nó. Event vào Rabbit
/// rồi nằm đó; người dùng không nhận được link, không hoàn tất được trong TTL 10 phút, mà giao
/// diện vẫn báo "đã gửi". Đây là kiểu hỏng khó lần nhất: mọi tầng đều báo thành công.
/// <para>
/// <c>ConfirmUrl</c> do AuthService dựng sẵn (đã kèm token) — consumer KHÔNG tự ghép URL, để chỉ
/// có một nơi duy nhất quyết định địa chỉ đích.
/// </para>
/// </remarks>
public class SendTwoFactorCrossDeviceConfirmConsumer : IConsumer<SendTwoFactorCrossDeviceConfirmEmailEvent>
{
    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendTwoFactorCrossDeviceConfirmConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendTwoFactorCrossDeviceConfirmConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendTwoFactorCrossDeviceConfirmConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendTwoFactorCrossDeviceConfirmEmailEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendTwoFactorCrossDeviceConfirmConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";
                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.FullName) ? msg.ToEmail : msg.FullName,
                    ["Email"] = msg.ToEmail,
                    ["ConfirmUrl"] = msg.ConfirmUrl,
                    ["ExpiresInMinutes"] = msg.ExpiresInMinutes.ToString(),
                };

                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.TwoFactorCrossDeviceConfirm,
                    values,
                    context.CancellationToken);

                var subject = $"Confirm Two-Factor Authentication — {appName}";
                await _emailSender.SendAsync(msg.ToEmail, subject, htmlBody, context.CancellationToken);

                // KHÔNG log ConfirmUrl: nó chứa token dùng được để bật 2FA. Log ra là biến file log
                // thành một đường vòng qua chính lớp bảo vệ đang được bật.
                _logger.LogInformation(
                    "2FA cross-device confirm email sent to {Email} (TTL {Minutes} phút).",
                    msg.ToEmail, msg.ExpiresInMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send 2FA cross-device confirm email to {Email}.", msg.ToEmail);
                throw;   // ProcessOnceAsync nhả chỗ giữ ⇒ MassTransit thử lại thật (GH-764).
            }
        });
    }
}
