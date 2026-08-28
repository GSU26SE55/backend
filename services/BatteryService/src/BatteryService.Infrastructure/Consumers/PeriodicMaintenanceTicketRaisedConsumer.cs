using BatteryService.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Nối kỳ bảo trì với ticket mà TicketService đã mở cho kỳ đó.
/// </summary>
/// <remarks>
/// <para>
/// Nửa quay về của <see cref="MaintenanceCycleDueEvent"/>. Kỳ được ghi trước, ticket sinh ra
/// sau, nên <c>maintenance_cycles.ticket_id</c> không thể điền lúc INSERT — consumer này lấp
/// vào khi TicketService báo đã mở ticket. Nhờ đó trang lịch sử bảo trì của pin mở thẳng
/// được sang ticket đã xử lý kỳ ấy.
/// </para>
/// <para>
/// Chỉ ghi khi cột còn trống. Kỳ đã có ticket khác nghĩa là dữ liệu mâu thuẫn — ghi đè sẽ
/// làm mất liên kết đúng và không để lại dấu vết, nên ở đây ghi log cảnh báo rồi bỏ qua để
/// người vận hành đối chiếu.
/// </para>
/// </remarks>
public class PeriodicMaintenanceTicketRaisedConsumer
    : IConsumer<PeriodicMaintenanceTicketRaisedEvent>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<PeriodicMaintenanceTicketRaisedConsumer> _logger;

    public PeriodicMaintenanceTicketRaisedConsumer(
        IBatteryUnitOfWork unitOfWork,
        IInboxStore inboxStore,
        ILogger<PeriodicMaintenanceTicketRaisedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PeriodicMaintenanceTicketRaisedEvent> context)
    {
        await context.ProcessOnceAsync(
            _inboxStore,
            nameof(PeriodicMaintenanceTicketRaisedConsumer),
            async () =>
            {
                var evt = context.Message;
                var ct = context.CancellationToken;

                var cycle = await _unitOfWork.MaintenanceCycles.GetAllAsync()
                    .FirstOrDefaultAsync(
                        item => item.Id == evt.MaintenanceCycleId && !item.IsDeleted, ct);

                if (cycle is null)
                {
                    _logger.LogWarning(
                        "Maintenance cycle {CycleId} not found — cannot link ticket {TicketCode}.",
                        evt.MaintenanceCycleId, evt.TicketCode);
                    return;
                }

                // Sự kiện giao lại lần hai: đã nối đúng ticket này rồi thì không có gì để làm.
                if (cycle.TicketId == evt.TicketId)
                    return;

                if (cycle.TicketId is { } existing)
                {
                    _logger.LogWarning(
                        "Maintenance cycle {CycleId} already links ticket {ExistingTicketId}; "
                        + "refusing to overwrite with {IncomingTicketId} ({TicketCode}).",
                        evt.MaintenanceCycleId, existing, evt.TicketId, evt.TicketCode);
                    return;
                }

                // Pin lệch nghĩa là hai luồng dữ liệu đã lạc nhau — nối vào sẽ gắn ticket của
                // pin này lên nhật ký của pin khác.
                if (cycle.BatteryAssetId != evt.BatteryAssetId)
                {
                    _logger.LogWarning(
                        "Maintenance cycle {CycleId} belongs to battery {CycleBatteryId} but "
                        + "ticket {TicketCode} reports {EventBatteryId} — skipping link.",
                        evt.MaintenanceCycleId, cycle.BatteryAssetId,
                        evt.TicketCode, evt.BatteryAssetId);
                    return;
                }

                cycle.TicketId = evt.TicketId;
                _unitOfWork.MaintenanceCycles.UpdateAsync(cycle);
                await _unitOfWork.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Linked maintenance cycle {CycleId} to ticket {TicketCode}.",
                    evt.MaintenanceCycleId, evt.TicketCode);
            });
    }
}
