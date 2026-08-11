using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Sprint IoT-2 #IoT2-31 (paired) — resolved event để clear in-app banner.
/// GH-604: recipient resolve qua <see cref="IRecipientResolver"/> (broadcast Manager + Admin).
/// Channel chỉ InApp (không spam Push/SMS lúc đã xử lý).
/// </summary>
public class NotificationEnvironmentalIncidentResolvedConsumer : IConsumer<EnvironmentalIncidentResolvedEvent>
{
    private readonly IMediator _mediator;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<NotificationEnvironmentalIncidentResolvedConsumer> _logger;

    public NotificationEnvironmentalIncidentResolvedConsumer(
        IMediator mediator,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<NotificationEnvironmentalIncidentResolvedConsumer> logger)
    {
        _mediator = mediator;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentResolvedEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "EnvironmentalIncidentResolved", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager", "Admin");
            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No Manager/Admin recipient resolved for EnvironmentalIncidentResolved incident={IncidentId} — skip.", evt.IncidentId);
                return;
            }

            // Bỏ Guid trần và định dạng `:O` khỏi câu hiển thị (IncidentId/SiteId không nói gì với
            // người đọc, và 2026-08-03T14:15:00.0000000Z thì không ai đọc được).
            // Event không mang tên site nên tiêu đề nói theo sự việc thay vì cố in SiteId ra.
            var title = evt.WasFalseAlarm
                ? "Environmental incident: false alarm"
                : "Environmental incident resolved";

            var opening = evt.WasFalseAlarm
                ? $"The environmental incident was determined to be a false alarm at {evt.ResolvedAt:HH:mm dd/MM/yyyy}."
                : $"The environmental incident was resolved at {evt.ResolvedAt:HH:mm dd/MM/yyyy}.";

            var note = string.IsNullOrWhiteSpace(evt.ResolutionNote)
                ? string.Empty
                : $" Note: {evt.ResolutionNote}";

            var body = opening + note;

            // Trước đây consumer này không gửi PayloadJson nên khi đã bỏ Guid khỏi câu chữ thì
            // không còn đường nào tra ra sự cố gốc — đưa định danh xuống đây.
            var payload = JsonSerializer.Serialize(new
            {
                incidentId = evt.IncidentId,
                siteId = evt.SiteId,
                wasFalseAlarm = evt.WasFalseAlarm,
                resolvedAt = evt.ResolvedAt,
            });

            foreach (var userId in recipientIds)
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.EnvironmentalIncidentResolved,
                    Channel = NotificationChannelEnum.InApp,
                    Title = title,
                    Body = body,
                    PayloadJson = payload,
                    EntityType = "EnvironmentalIncident",
                    EntityId = evt.IncidentId
                };
                var result = await _mediator.Send(cmd, context.CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create EnvironmentalIncident resolved notification incident={IncidentId}: {Message}",
                        evt.IncidentId, result.Message);
                }
            }
        });
    }
}
