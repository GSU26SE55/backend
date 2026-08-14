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
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "TicketAssigned", _logger, async () =>
        {
            var evt = context.Message;

            await TicketSchedulingNotificationWriter.WriteAssignmentAsync(
                _unitOfWork, evt.TicketId, evt.Code, evt.CustomerId,
                evt.PrimaryHandlerStaffId, evt.Priority, evt.ScheduledStartAtUtc,
                evt.ScheduleVersion, evt.WorkStartsImmediately, context.CancellationToken);

            if (evt.CustomerId == Guid.Empty)
            {
                _logger.LogWarning(
                    "TicketAssigned ticket={TicketId}: CustomerId rỗng — bỏ qua notification cho Customer.", evt.TicketId);
            }
        });
    }
}
