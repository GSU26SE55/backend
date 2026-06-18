using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Email channel — publish <see cref="SendNotificationEmailEvent"/> lên RabbitMQ.
/// EmailService consume và gửi email thực tế — template rendering xử lý ở #111.
/// </summary>
public class EmailBusChannel : INotificationChannel
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<EmailBusChannel> _logger;

    public EmailBusChannel(IPublishEndpoint publishEndpoint, ILogger<EmailBusChannel> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Email;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            _logger.LogWarning("EmailBusChannel: no email for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, "No email address");
        }

        try
        {
            await _publishEndpoint.Publish(
                new SendNotificationEmailEvent(
                    NotificationId: request.NotificationId,
                    ToEmail: request.Email,
                    Subject: request.Title,
                    Body: request.Body,
                    SourceService: "notification"
                ),
                ct);

            return new ChannelResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailBusChannel: failed to publish email for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, ex.Message);
        }
    }
}
