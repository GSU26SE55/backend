using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SmsService.Application.CQRS.Command.Sms;

namespace SmsService.Application.Consumers;

/// <summary>
/// Inbound contract chuẩn: bất kỳ service nào (Auth/Battery/Ticket/Notification) publish
/// <see cref="SendSmsCommand"/> để gửi SMS. Consumer forward sang <see cref="QueueSmsCommand"/>
/// nội bộ; Inbox dedup theo <c>(consumerName, messageId)</c>.
/// </summary>
public class SendSmsCommandConsumer : IConsumer<SendSmsCommand>
{
    private readonly IMediator _mediator;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<SendSmsCommandConsumer> _logger;

    public SendSmsCommandConsumer(IMediator mediator, IInboxStore inboxStore, ILogger<SendSmsCommandConsumer> logger)
    {
        _mediator = mediator;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendSmsCommand> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendSmsCommandConsumer), async () =>
        {
            var result = await _mediator.Send(new QueueSmsCommand
            {
                PhoneNumber = msg.PhoneNumber,
                Message = msg.Message,
                SourceService = msg.SourceService,
                CorrelationId = msg.CorrelationId,
                Category = msg.Category,
                TargetDeviceCode = msg.TargetDeviceCode
            }, context.CancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("QueueSms rejected: {Message} (corr={Corr}, source={Source})",
                    result.Message, msg.CorrelationId, msg.SourceService);
            }
            else
            {
                _logger.LogInformation("SMS queued id={SmsId} corr={Corr} source={Source}",
                    result.Data, msg.CorrelationId, msg.SourceService);
            }
        });
    }
}
