using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

public class SendPasswordResetOtpConsumer : IConsumer<SendPasswordResetOtpEvent>
{
    private const int OtpExpireMinutes = 10;

    private readonly EmailSenderService _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendPasswordResetOtpConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendPasswordResetOtpConsumer(
        EmailSenderService emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendPasswordResetOtpConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendPasswordResetOtpEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendPasswordResetOtpConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";

                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.ToEmail) ? "bạn" : msg.ToEmail,
                    ["Otp"] = msg.Otp,
                    ["ExpireMinutes"] = OtpExpireMinutes.ToString()
                };

                string htmlBody;
                try
                {
                    htmlBody = await _templateRenderer.RenderAsync(
                        EmailTemplates.OtpPasswordReset,
                        values,
                        context.CancellationToken);
                }
                catch (FileNotFoundException)
                {
                    // Fallback nếu chưa có template riêng
                    htmlBody = await _templateRenderer.RenderAsync(
                        EmailTemplates.OtpRegister,
                        values,
                        context.CancellationToken);
                }

                var subject = $"Reset password OTP - {appName}";
                await _emailSender.SendAsync(msg.ToEmail, subject, htmlBody, context.CancellationToken);
                _logger.LogInformation("Password reset OTP email sent to {Email}.", msg.ToEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset OTP to {Email}.", msg.ToEmail);
                throw;
            }
        });
    }
}
