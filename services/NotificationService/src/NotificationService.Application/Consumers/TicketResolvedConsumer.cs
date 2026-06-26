using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Ticket resolved → notify Manager. GH-604: recipient resolve qua <see cref="IRecipientResolver"/>
/// (broadcast Manager). Customer cụ thể của ticket bị DEFER (event chỉ mang StaffId người resolve,
/// không có CustomerId) — follow-up enrich event sau. Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class TicketResolvedConsumer : IConsumer<TicketResolvedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketResolvedConsumer> _logger;

    public TicketResolvedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketResolvedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketResolvedEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate TicketResolved message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No Manager recipient resolved for TicketResolved ticket={TicketId} — skip.", evt.TicketId);
            return;
        }

        var title = $"Ticket {evt.Code} đã được xử lý";
        var body = string.IsNullOrWhiteSpace(evt.ResolutionSummary)
            ? $"Ticket {evt.Code} đã được resolve."
            : $"Ticket {evt.Code} đã được resolve: {evt.ResolutionSummary}";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            resolvedByStaffId = evt.StaffId,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketResolved, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}
