using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

public class SendEmailChangeOtpConsumer : IConsumer<SendEmailChangeOtpEvent>
{
    private const int OtpExpireMinutes = 10;

    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendEmailChangeOtpConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendEmailChangeOtpConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendEmailChangeOtpConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendEmailChangeOtpEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendEmailChangeOtpConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";
                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.ToNewEmail) ? "there" : msg.ToNewEmail,
                    ["Otp"] = msg.Otp,
                    ["ExpireMinutes"] = OtpExpireMinutes.ToString(),
                    // Sprint 6.2 NOTI-09 (#680) — template OtpEmailChange.html mới hiển thị địa chỉ
                    // email đích; thiếu key này thì placeholder sẽ hiện nguyên văn trong email.
                    ["PendingEmail"] = msg.ToNewEmail
                };

                string htmlBody;
                try
                {
                    htmlBody = await _templateRenderer.RenderAsync(EmailTemplates.OtpEmailChange, values, context.CancellationToken);
                }
                catch (FileNotFoundException)
                {
                    htmlBody = await _templateRenderer.RenderAsync(EmailTemplates.OtpRegister, values, context.CancellationToken);
                }

                var subject = $"Confirm new email - {appName}";
                await _emailSender.SendAsync(msg.ToNewEmail, subject, htmlBody, context.CancellationToken);
                _logger.LogInformation("Email-change OTP sent to {Email}.", msg.ToNewEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email-change OTP to {Email}.", msg.ToNewEmail);
                throw;
            }
        });
    }
}
