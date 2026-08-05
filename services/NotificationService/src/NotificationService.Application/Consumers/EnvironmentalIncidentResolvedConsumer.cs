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
public class EnvironmentalIncidentResolvedConsumer : IConsumer<EnvironmentalIncidentResolvedEvent>
{
    private readonly IMediator _mediator;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<EnvironmentalIncidentResolvedConsumer> _logger;

    public EnvironmentalIncidentResolvedConsumer(
        IMediator mediator,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<EnvironmentalIncidentResolvedConsumer> logger)
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

            var label = evt.WasFalseAlarm ? "false-alarm" : "resolved";
            var title = $"[ENV] Đã {label} — site {evt.SiteId}";
            var body = $"Sự cố môi trường (IncidentId {evt.IncidentId}) đã được đánh dấu {label} lúc {evt.ResolvedAt:O}. " +
                       $"{evt.ResolutionNote ?? string.Empty}";

            foreach (var userId in recipientIds)
            {
                var cmd = new CreateNotificationCommand
                {
                    UserId = userId,
                    Type = NotificationTypeEnum.EnvironmentalIncidentResolved,
                    Channel = NotificationChannelEnum.InApp,
                    Title = title,
                    Body = body,
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
