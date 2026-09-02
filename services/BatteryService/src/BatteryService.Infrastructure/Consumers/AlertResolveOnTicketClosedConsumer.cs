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
///
/// Tên class KHÔNG được trùng <c>NotificationService.Application.Consumers.TicketClosedConsumer</c>
/// — MassTransit đặt tên receive-endpoint theo tên consumer type (không theo namespace/service),
/// nên 2 consumer trùng tên ở 2 service khác nhau sẽ vô tình bind chung 1 queue và cạnh tranh
/// message thay vì mỗi service nhận đủ bản riêng.
/// </summary>
public class AlertResolveOnTicketClosedConsumer : IConsumer<TicketClosedEvent>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly ILogger<AlertResolveOnTicketClosedConsumer> _logger;

    public AlertResolveOnTicketClosedConsumer(IBatteryUnitOfWork uow, ILogger<AlertResolveOnTicketClosedConsumer> logger)
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

        foreach (var alert in alerts)
        {
            alert.Status = AlertStatusEnum.Resolved;
            // Dùng timestamp nghiệp vụ từ event thay vì thời điểm consumer xử lý. Mốc ổn định
            // này cho phép lần TicketReopened kế tiếp chỉ mở lại đúng alert của close cycle đó.
            alert.ResolvedAt = msg.ClosedAt;
            _uow.Alerts.UpdateAsync(alert);
        }

        await _uow.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Ticket {TicketId} closed — resolved {Count} linked alert(s).",
            msg.TicketId, alerts.Count);
    }
}
