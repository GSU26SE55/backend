using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — consumer cho các event vòng đời Ticket. Ghi notification trực tiếp qua UnitOfWork
/// (InApp + Push). Recipient: dùng id thật khi event cung cấp; còn lại Guid.Empty
/// (TODO Sprint 6 — AccountSyncReadModel resolve Manager/Customer; dispatcher fan-out).
/// </summary>
public class TicketCreatedConsumer : IConsumer<TicketCreatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketCreatedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketCreatedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Manager user IDs.
        var recipientIds = new[] { Guid.Empty };

        var title = $"Ticket mới: {evt.Code}";
        var body = $"Ticket {evt.Code} vừa được tạo và đang chờ phân công.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketCreated, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}

/// <summary>
/// Ticket được assign/reassign Staff → notify chính Staff đó (StaffId có sẵn trong event).
/// </summary>
public class TicketAssignedConsumer : IConsumer<TicketAssignedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketAssignedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketAssignedEvent> context)
    {
        var evt = context.Message;

        var recipientIds = new[] { evt.StaffId };

        var title = $"Bạn được phân công ticket {evt.Code}";
        var body = $"Ticket {evt.Code} (ưu tiên {evt.Priority}) đã được giao cho bạn.";
        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            code = evt.Code,
            staffId = evt.StaffId,
            priority = evt.Priority,
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.TicketAssigned, NotificationWriter.InAppPush,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }
}

/// <summary>
/// Ticket resolved → notify Customer + Manager (placeholder Guid.Empty).
/// StaffId trong event là người resolve, KHÔNG phải recipient.
/// </summary>
public class TicketResolvedConsumer : IConsumer<TicketResolvedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public TicketResolvedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<TicketResolvedEvent> context)
    {
        var evt = context.Message;

        // TODO Sprint 6 — resolve Customer + Manager user IDs.
        var recipientIds = new[] { Guid.Empty };

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
