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
///
/// Sprint 6.2 NOTI-05 (#676) — event nay mang thêm <c>CustomerId</c> nên notify được CẢ Customer
/// ("Staff đang xử lý sự cố của bạn") đúng spec §3.4. Trước đó phần Customer bị bỏ trống với comment
/// "deferred (event lacks CustomerId)" (reviewnotification.md §4.1).
/// Staff nhận InApp+Push+Email (kèm SLA); Customer nhận InApp+Push+Email.
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

        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            staffId = evt.PrimaryHandlerStaffId,
            customerId = evt.CustomerId,
            priority = evt.Priority,
            screen = "TicketDetail"
        });

        // Staff được phân công.
        await NotificationWriter.WriteAsync(
            _unitOfWork, [evt.PrimaryHandlerStaffId], NotificationTypeEnum.TicketAssigned, NotificationWriter.InAppPushEmail,
            $"Bạn được phân công ticket {evt.Code}",
            $"Ticket {evt.Code} (ưu tiên {evt.Priority}) đã được giao cho bạn.",
            payload, "Ticket", evt.TicketId, context.CancellationToken);

        // Sprint 6.2 NOTI-05 (#676) — Customer sở hữu ticket.
        if (evt.CustomerId != Guid.Empty && evt.CustomerId != evt.PrimaryHandlerStaffId)
        {
            await NotificationWriter.WriteAsync(
                _unitOfWork, [evt.CustomerId], NotificationTypeEnum.TicketAssigned, NotificationWriter.InAppPushEmail,
                $"Ticket {evt.Code} đã có nhân viên xử lý",
                $"Yêu cầu {evt.Code} của bạn đã được phân công cho nhân viên kỹ thuật (ưu tiên {evt.Priority}).",
                payload, "Ticket", evt.TicketId, context.CancellationToken);
        }
        else if (evt.CustomerId == Guid.Empty)
        {
            _logger.LogWarning(
                "TicketAssigned ticket={TicketId}: CustomerId rỗng — bỏ qua notification cho Customer.", evt.TicketId);
        }
    }
}
