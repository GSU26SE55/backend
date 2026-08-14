using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

public class SendOtpRegisterConsumer : IConsumer<SendOtpRegisterEvent>
{
    private const int OtpExpireMinutes = 5;

    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly IInboxStore _inboxStore;

    public SendOtpRegisterConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendOtpRegisterEvent> context)
    {
        var msg = context.Message;
        Console.WriteLine($"[RabbitMQ] Received request to send email to: {msg.ToEmail}");

        await context.ProcessOnceAsync(_inboxStore, nameof(SendOtpRegisterConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Event Management";

                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.ToEmail) ? "there" : msg.ToEmail,
                    ["Otp"] = msg.Otp,
                    ["ExpireMinutes"] = OtpExpireMinutes.ToString()
                };

                var subject = $"Verify registering account - {appName}";
                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.OtpRegister,
                    values,
                    context.CancellationToken);

                await _emailSender.SendAsync(msg.ToEmail, subject, htmlBody, context.CancellationToken);
                Console.WriteLine($"[Success] Email sent to {msg.ToEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to send email: {ex.Message}");
                throw;
            }
        });
    }
}
