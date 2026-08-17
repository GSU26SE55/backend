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

            var audience = evt.PauseReason == 1 ? "Customer" : "Manager";
            var code = string.IsNullOrWhiteSpace(evt.Code) ? string.Empty : $" {evt.Code}";
            await NotificationWriter.WriteAsync(_unitOfWork, recipients, NotificationTypeEnum.SlaAutoResumed,
                NotificationWriter.InAppPush, $"SLA auto-resumed{code}",
                $"Ticket{code} SLA has auto-resumed and requires {audience} action.",
                // resumedAt đã định dạng: template in thẳng biến này ra câu chữ, mà bộ render
                // Handlebars không có helper format date — giá trị thô sẽ hiện dạng ISO.
                JsonSerializer.Serialize(new
                {
                    ticketId = evt.TicketId,
                    code = evt.Code,
                    resumedAt = evt.ResumedAt.ToString("HH:mm dd/MM/yyyy")
                }),
                "Ticket", evt.TicketId, context.CancellationToken);
        });

        if (!processed)
            _logger.LogInformation("Debounce: skip duplicate SlaAutoResumed event pauseEvent={SlaPauseEventId}",
                context.Message.SlaPauseEventId);
    }
}
