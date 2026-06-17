using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SmsService.Application.CQRS.Command.Sms;

namespace SmsService.Application.Consumers;

/// <summary>
/// Backward-compat consumer cho AuthService cũ vẫn publish <see cref="SendPhoneOtpEvent"/>.
/// Render template OTP và forward sang <see cref="QueueSmsCommand"/>. Sau khi AuthService migrate
/// sang <see cref="SendSmsCommand"/> (Phase 9) và verify 1-2 sprint không còn event này nữa,
/// XÓA class này.
/// </summary>
public class SendPhoneOtpConsumer : IConsumer<SendPhoneOtpEvent>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<SendPhoneOtpConsumer> _logger;

    public SendPhoneOtpConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<SendPhoneOtpConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendPhoneOtpEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendPhoneOtpConsumer), async () =>
        {
            var body = $"Ma OTP cua ban la {msg.Otp}. Vui long khong chia se ma nay.";

            var result = await _mediator.Send(new QueueSmsCommand
            {
                PhoneNumber = msg.PhoneNumber,
                Message = body,
                SourceService = "auth",
                CorrelationId = msg.Id,
                Category = "otp"
            }, context.CancellationToken);

            _logger.LogInformation(
                "PhoneOtp consumed → QueueSms result={Success} smsId={SmsId} phone={Phone}",
                result.IsSuccess, result.Data, msg.PhoneNumber);
        });
    }
}
