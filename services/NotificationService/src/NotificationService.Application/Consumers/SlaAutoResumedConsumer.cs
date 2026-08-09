using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

public class SlaAutoResumedConsumer : IConsumer<SlaAutoResumedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<SlaAutoResumedConsumer> _logger;

    public SlaAutoResumedConsumer(INotificationUnitOfWork unitOfWork, IRecipientResolver recipientResolver,
        ICacheService cache, ILogger<SlaAutoResumedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaAutoResumedEvent> context)
    {
        var processed = await NotificationDebounce.ProcessOnceByMessageAsync(
            _cache, context.Message.SlaPauseEventId, context.CancellationToken, async () =>
        {
            var evt = context.Message;
            var recipients = evt.PauseReason switch
            {
                1 => new List<Guid> { evt.CustomerId },
                2 => (await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager")).ToList(),
                _ => []
            };

            recipients = recipients.Where(id => id != Guid.Empty).Distinct().ToList();
            if (recipients.Count == 0)
                return;

            var audience = evt.PauseReason == 1 ? "Khách hàng" : "Quản lý";
            var code = string.IsNullOrWhiteSpace(evt.Code) ? string.Empty : $" {evt.Code}";
            await NotificationWriter.WriteAsync(_unitOfWork, recipients, NotificationTypeEnum.SlaAutoResumed,
                NotificationWriter.InAppPush, $"SLA đã tự tiếp tục{code}",
                $"Ticket{code} đã tự tiếp tục SLA và cần được {audience} xử lý.",
                JsonSerializer.Serialize(new { ticketId = evt.TicketId, code = evt.Code, resumedAt = evt.ResumedAt }),
                "Ticket", evt.TicketId, context.CancellationToken);
        });

        if (!processed)
            _logger.LogInformation("Debounce: skip duplicate SlaAutoResumed event pauseEvent={SlaPauseEventId}",
                context.Message.SlaPauseEventId);
    }
}
