using MassTransit;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SmsService.Infrastructure.Services;

namespace SmsService.Infrastructure.Consumers;

public class SendPhoneOtpConsumer : IConsumer<SendPhoneOtpEvent>
{
    private readonly ISmsSender _smsSender;
    private readonly ILogger<SendPhoneOtpConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendPhoneOtpConsumer(ISmsSender smsSender, ILogger<SendPhoneOtpConsumer> logger, IInboxStore inboxStore)
    {
        _smsSender = smsSender;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendPhoneOtpEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendPhoneOtpConsumer), async () =>
        {
            var body = $"Ma OTP cua ban la {msg.Otp}. Vui long khong chia se ma nay.";

            await _smsSender.SendAsync(msg.PhoneNumber, body, context.CancellationToken);
            _logger.LogInformation("Phone OTP SMS sent to {PhoneNumber}.", msg.PhoneNumber);
        });
    }
}
