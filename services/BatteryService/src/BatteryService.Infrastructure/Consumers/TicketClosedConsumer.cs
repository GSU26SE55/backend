using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Khi Manager đóng Ticket, resolve các Alert liên kết mà <c>AlertAutoResolveService</c>
/// không tự resolve được — Env/Device (BatteryAssetId null hoặc DeviceOffline),
/// SohDegradation, SensorMismatch — vì không có tín hiệu sensor đáng tin để suy luận lại
/// anomaly đã hết. Ticket Closed là xác nhận nghiệp vụ cuối cùng của con người nên không
/// cần kiểm tra lại anomaly, khác với AlertAutoResolveService.
/// </summary>
public class TicketClosedConsumer : IConsumer<TicketClosedEvent>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly ILogger<TicketClosedConsumer> _logger;

    public TicketClosedConsumer(IBatteryUnitOfWork uow, ILogger<TicketClosedConsumer> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketClosedEvent> context)
    {
        var msg = context.Message;

        var alerts = await _uow.Alerts.GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.TicketId == msg.TicketId
                        && a.Status != AlertStatusEnum.Resolved)
            .ToListAsync(context.CancellationToken);

        if (alerts.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var alert in alerts)
        {
            alert.Status = AlertStatusEnum.Resolved;
            alert.ResolvedAt = now;
            _uow.Alerts.UpdateAsync(alert);
        }

        await _uow.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Ticket {TicketId} closed — resolved {Count} linked alert(s).",
            msg.TicketId, alerts.Count);
    }
}
