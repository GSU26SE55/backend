using MassTransit;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

/// <summary>
/// STUB consumer cho phone OTP. Hiện tại chỉ log; sau này swap sang SMS provider (Twilio/Vonage/...).
/// Để thay đổi, inject ISmsSender vào đây và thay LogInformation bằng SmsSender.SendAsync.
/// </summary>
[ExcludeFromConfigureEndpoints]
public class SendPhoneOtpConsumer : IConsumer<SendPhoneOtpEvent>
{
    private readonly ILogger<SendPhoneOtpConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendPhoneOtpConsumer(ILogger<SendPhoneOtpConsumer> logger, IInboxStore inboxStore)
    {
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendPhoneOtpEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendPhoneOtpConsumer), () =>
        {
            _logger.LogInformation(
                "[STUB SMS] Phone OTP for {Phone} = {Otp}. (Replace with real SMS provider when ready.)",
                msg.PhoneNumber,
                msg.Otp);
            return Task.CompletedTask;
        });
    }
}
