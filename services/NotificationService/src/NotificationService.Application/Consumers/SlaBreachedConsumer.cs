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
/// GH-107 — SLA timer đã breach → notify Manager + Admin. GH-604: recipient resolve qua
/// <see cref="IRecipientResolver"/>.
///
/// Sprint 6.2 NOTI-06 (#677) — PHÂN NHÁNH THEO PRIORITY đúng spec §3.4 / design.md.
/// Trước đó mọi priority xử lý giống hệt nhau (Manager+Admin, InApp+Push) dù payload đã có
/// <c>Priority</c> (reviewnotification.md §4.2):
///
/// <list type="table">
/// <item><term>P1 Critical</term><description>Manager + Admin · InApp + Push + Email + SMS (kèm escalate — do
/// <c>EscalationBackgroundService</c> bên TicketService thực hiện).</description></item>
/// <item><term>P2 High</term><description>Manager · InApp + Push + Email (KHÔNG SMS).</description></item>
/// <item><term>P3 Standard</term><description>Manager · chỉ InApp (không push/email).</description></item>
/// </list>
///
/// Lưu ý: priority KHÔNG bị đổi khi breach (Priority Policy trong <c>.claude/rules/design.md</c>) —
/// breach chỉ thêm nhân lực/kênh báo, không đổi deadline.
/// </summary>
public class SlaBreachedConsumer : IConsumer<SlaBreachedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<SlaBreachedConsumer> _logger;

    public SlaBreachedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<SlaBreachedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaBreachedEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate SlaBreached message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;
        var tier = ResolvePriorityTier(evt.Priority);

        var roles = tier == PriorityTier.P1
            ? new[] { "Manager", "Admin" }
            : new[] { "Manager" };

        var recipientIds = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, roles);
        if (recipientIds.Count == 0)
        {
            _logger.LogWarning("No {Roles} recipient resolved for SlaBreached ticket={TicketId} — skip.",
                string.Join("/", roles), evt.TicketId);
            return;
        }

        var channels = ResolveChannels(tier);

        var title = tier switch
        {
            PriorityTier.P1 => "🔴 SLA P1 bị vi phạm — cần xử lý ngay",
            PriorityTier.P2 => "🟠 SLA P2 bị vi phạm",
            _ => "🟡 SLA P3 bị vi phạm",
        };

        var body = tier switch
        {
            PriorityTier.P1 =>
                $"Ticket ưu tiên {evt.Priority} đã breach SLA lúc {evt.BreachedAt:dd/MM HH:mm}. " +
                "Cần reassign Senior (Tier 3) và báo Admin ngay.",
            PriorityTier.P2 =>
                $"Ticket ưu tiên {evt.Priority} đã breach SLA lúc {evt.BreachedAt:dd/MM HH:mm}. " +
                "Manager cân nhắc reassign Tier 2/3.",
            _ =>
                $"Ticket ưu tiên {evt.Priority} đã breach SLA lúc {evt.BreachedAt:dd/MM HH:mm}. " +
                "Cần Manager review khi có thể.",
        };

        var payload = JsonSerializer.Serialize(new
        {
            ticketId = evt.TicketId,
            breachedAt = evt.BreachedAt,
            priority = evt.Priority,
            priorityTier = tier.ToString(),
            screen = "TicketDetail"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.SlaBreached, channels,
            title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
    }

    private static NotificationChannelEnum[] ResolveChannels(PriorityTier tier) => tier switch
    {
        PriorityTier.P1 =>
        [
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email,
            NotificationChannelEnum.Sms,
        ],
        PriorityTier.P2 =>
        [
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email,
        ],
        _ => [NotificationChannelEnum.InApp],
    };

    /// <summary>
    /// Payload mang priority dạng chuỗi (<c>TicketPriorityEnum.ToString()</c> = "P1Critical" /
    /// "P2High" / "P3Normal"). Nhận diện theo tiền tố để không phụ thuộc TicketService.Domain.
    /// Không đọc được → coi như P3 (mức ồn ào thấp nhất, tránh bắn SMS vì dữ liệu lạ).
    /// </summary>
    private static PriorityTier ResolvePriorityTier(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return PriorityTier.P3;

        var value = priority.Trim();
        if (value.StartsWith("P1", StringComparison.OrdinalIgnoreCase))
            return PriorityTier.P1;
        if (value.StartsWith("P2", StringComparison.OrdinalIgnoreCase))
            return PriorityTier.P2;

        return PriorityTier.P3;
    }

    private enum PriorityTier
    {
        P1 = 1,
        P2 = 2,
        P3 = 3,
    }
}
