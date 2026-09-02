using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Đối xứng với <see cref="AlertResolveOnTicketClosedConsumer"/>: Customer reopen Ticket trong
/// cửa sổ 7 ngày nghĩa là sự cố CHƯA thực sự xong dù ticket đã từng Closed. Alert liên kết bị
/// resolve bởi lần Close trước đó cần quay lại Open để phản ánh đúng — nếu không, Alert list
/// sẽ "sạch" trong khi ticket đang được xử lý lại.
///
/// Tên class KHÔNG được trùng <c>NotificationService.Application.Consumers.TicketReopenedConsumer</c>
/// — cùng lý do đặt tên như <see cref="AlertResolveOnTicketClosedConsumer"/>.
/// </summary>
public class AlertReopenOnTicketReopenedConsumer : IConsumer<TicketReopenedEvent>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly ILogger<AlertReopenOnTicketReopenedConsumer> _logger;

    public AlertReopenOnTicketReopenedConsumer(IBatteryUnitOfWork uow, ILogger<AlertReopenOnTicketReopenedConsumer> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketReopenedEvent> context)
    {
        var msg = context.Message;

        var alerts = await _uow.Alerts.GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.TicketId == msg.TicketId
                        && a.Status == AlertStatusEnum.Resolved)
            .ToListAsync(context.CancellationToken);

        if (alerts.Count == 0)
            return;

        foreach (var alert in alerts)
        {
            alert.Status = AlertStatusEnum.Open;
            alert.ResolvedAt = null;
            _uow.Alerts.UpdateAsync(alert);
        }

        await _uow.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Ticket {TicketId} reopened — reverted {Count} linked alert(s) back to Open.",
            msg.TicketId, alerts.Count);
    }
}
