using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Events.Root;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// SMS channel — publish <see cref="SendSmsCommand"/> lên RabbitMQ.
/// SmsService consume và gửi SMS thực tế qua gateway.
/// </summary>
public class SmsBusChannel : INotificationChannel
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SmsBusChannel> _logger;

    public SmsBusChannel(IPublishEndpoint publishEndpoint, ILogger<SmsBusChannel> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Sms;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            _logger.LogWarning("SmsBusChannel: no phone number for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, "No phone number");
        }

        try
        {
            await _publishEndpoint.Publish(
                new SendSmsCommand(
                    PhoneNumber: request.PhoneNumber,
                    Message: request.Body,
                    SourceService: "notification",
                    CorrelationId: request.NotificationId
                )
                {
                    // GH-792 — ID sinh theo NotificationId, KHÔNG ngẫu nhiên. SmsService chống trùng
                    // bằng ProcessOnceAsync khoá theo Id này; ID ngẫu nhiên nghĩa là lần gửi lại
                    // (sau khi tiến trình chết giữa "đã publish" và "đã ghi Sent") trông như một
                    // việc mới và người dùng nhận SMS lần thứ hai.
                    Id = DeterministicEventId.From(request.NotificationId, "sms"),
                },
                ct);

            return new ChannelResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmsBusChannel: failed to publish SMS for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, ex.Message);
        }
    }
}
