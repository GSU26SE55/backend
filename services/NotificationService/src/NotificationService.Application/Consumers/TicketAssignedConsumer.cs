using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket được assign/reassign Staff → notify chính Staff đó (StaffId có sẵn trong event).
/// Ghi notification trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketAssignedConsumer : IConsumer<TicketAssignedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketAssignedConsumer> _logger;

    public TicketAssignedConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<TicketAssignedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketAssignedEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate TicketAssigned message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = new[] { evt.PrimaryHandlerStaffId };

        var title = $"Bạn được phân công ticket {evt.Code}";
        var body = $"Ticket {evt.Code} (ưu tiên {evt.Priority}) đã được giao cho bạn.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            staffId = evt.PrimaryHandlerStaffId,
            priority = evt.Priority,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketAssigned, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
