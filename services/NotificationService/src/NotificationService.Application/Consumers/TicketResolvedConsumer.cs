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
/// (broadcast Manager).
///
/// Sprint 6.2 NOTI-05 (#676) — event nay có <c>CustomerId</c> nên Customer cũng được báo
/// (trước đây phần này bị defer vì payload thiếu CustomerId — reviewnotification.md §4.1).
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
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "TicketResolved", _logger, async () =>
        {
            var evt = context.Message;

            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                resolvedByStaffId = evt.StaffId,
                customerId = evt.CustomerId,
                screen = "TicketDetail"
            });

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
            if (recipientIds.Count > 0)
            {
                var body = string.IsNullOrWhiteSpace(evt.ResolutionSummary)
                    ? $"Ticket {evt.Code} has been resolved."
                    : $"Ticket {evt.Code} has been resolved: {evt.ResolutionSummary}";

                await NotificationWriter.WriteAsync(
                    _unitOfWork, recipientIds, NotificationTypeEnum.TicketResolved, NotificationWriter.InAppPushEmail,
                    $"Ticket {evt.Code} has been handled", body, payload, "Ticket", evt.TicketId, context.CancellationToken);
            }
            else
            {
                _logger.LogWarning("No Manager recipient resolved for TicketResolved ticket={TicketId}.", evt.TicketId);
            }

            // Sprint 6.2 NOTI-05 (#676) — báo Customer là sự cố của họ đã xử lý xong, chờ Manager duyệt.
            if (evt.CustomerId != Guid.Empty)
            {
                var customerBody = string.IsNullOrWhiteSpace(evt.ResolutionSummary)
                    ? $"The issue in ticket {evt.Code} has been resolved and is awaiting manager approval."
                    : $"The issue in ticket {evt.Code} has been resolved: {evt.ResolutionSummary}. Awaiting manager approval.";

                await NotificationWriter.WriteAsync(
                    _unitOfWork, [evt.CustomerId], NotificationTypeEnum.TicketResolved, NotificationWriter.InAppPushEmail,
                    $"Ticket {evt.Code} has been resolved", customerBody, payload, "Ticket", evt.TicketId, context.CancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "TicketResolved ticket={TicketId}: CustomerId rỗng — bỏ qua notification cho Customer.", evt.TicketId);
            }
        });
    }
}
