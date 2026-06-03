using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

public class SendAdminInviteConsumer : IConsumer<SendAdminInviteEvent>
{
    private const string DefaultAcceptUrlBase = "http://localhost:5173/auth/accept-invite";

    private readonly EmailSenderService _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendAdminInviteConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendAdminInviteConsumer(
        EmailSenderService emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendAdminInviteConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendAdminInviteEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendAdminInviteConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";
                var acceptUrl = BuildAcceptUrl(msg.InvitationToken);
                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.FullName) ? msg.Email : msg.FullName,
                    ["Email"] = msg.Email,
                    ["Role"] = msg.Role,
                    ["AcceptUrl"] = acceptUrl,
                    ["InvitationToken"] = msg.InvitationToken,
                    ["ExpiresAt"] = msg.ExpiresAt.ToString("yyyy-MM-dd HH:mm 'UTC'")
                };

                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.AdminInvite,
                    values,
                    context.CancellationToken);

                var subject = $"You're invited to {appName}";
                await _emailSender.SendAsync(msg.Email, subject, htmlBody, context.CancellationToken);
                _logger.LogInformation("Admin invite email sent to {Email} for account {AccountId}.", msg.Email, msg.AccountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send admin invite email to {Email}.", msg.Email);
                throw;
            }
        });
    }

    private string BuildAcceptUrl(string token)
    {
        var baseUrl = _configuration["AdminInvite:AcceptUrlBase"]
            ?? _configuration["Frontend:AcceptInviteUrl"]
            ?? DefaultAcceptUrlBase;

        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}token={Uri.EscapeDataString(token)}";
    }
}
