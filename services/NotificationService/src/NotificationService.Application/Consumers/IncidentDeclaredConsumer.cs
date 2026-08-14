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
/// GH-107 — Manager declare P1 Critical Incident từ Ticket → notify Manager + Admin.
/// GH-604: recipient resolve qua <see cref="IRecipientResolver"/> (broadcast Manager + Admin).
/// DeclaredByUserId là người declare, KHÔNG phải recipient. Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class IncidentDeclaredConsumer : IConsumer<IncidentDeclaredEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<IncidentDeclaredConsumer> _logger;

    public IncidentDeclaredConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<IncidentDeclaredConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IncidentDeclaredEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "IncidentDeclared", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for IncidentDeclared ticket={TicketId} — skip.", evt.TicketId);
                return;
            }

            var title = $"🚨 Critical Incident: {evt.Code}";
            var body = $"Ticket {evt.Code} has been declared a Critical Incident. Urgent action is required.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                declaredByUserId = evt.DeclaredByUserId,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds, NotificationTypeEnum.IncidentDeclared, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}
