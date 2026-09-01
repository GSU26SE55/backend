using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.Consumers;

/// <summary>
/// Sprint Bonus NS-22 (#662, E2) — consume <see cref="EnvironmentalIncidentResolvedEvent"/>:
/// nếu <c>WasFalseAlarm</c> → auto-close ticket gắn incident đó (đóng vòng). Sự cố được resolve
/// bình thường (không false alarm) để Staff đóng theo quy trình chuẩn — không đụng ở đây.
/// </summary>
public class TicketEnvironmentalIncidentResolvedConsumer : IConsumer<EnvironmentalIncidentResolvedEvent>
{
    private static readonly TicketStatusEnum[] ClosableStatuses =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Pending,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.Request,
        TicketStatusEnum.ReAssign
    };

    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<TicketEnvironmentalIncidentResolvedConsumer> _logger;

    public TicketEnvironmentalIncidentResolvedConsumer(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ILogger<TicketEnvironmentalIncidentResolvedConsumer> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentResolvedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!evt.WasFalseAlarm)
            return; // resolve thật → Staff đóng theo quy trình, không auto-close.

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => !t.IsDeleted
                && t.EnvironmentalIncidentId == evt.IncidentId
                && ClosableStatuses.Contains(t.Status), ct);
        if (ticket is null)
        {
            _logger.LogInformation(
                "False-alarm incident {IncidentId} — không có ticket active để đóng.", evt.IncidentId);
            return;
        }

        var oldStatus = ticket.Status;
        ticket.Status = TicketStatusEnum.ClosedRejected;
        ticket.ClosedAt = evt.ResolvedAt;
        ticket.Reason = "Environmental incident confirmed as a false alarm.";

        // Dừng SLA timer (không để background service breach ticket đã đóng).
        //
        // Stopped chứ KHÔNG phải Met: ticket đóng vì incident là báo động giả, tức SLA bị HUỶ chứ
        // không phải đã đạt. Đánh Met khiến ticket này được tính là "đúng hạn" trong SLA compliance
        // (met / (met + breach)) dù thực tế chẳng ai xử lý gì — thổi phồng chỉ số. Stopped cũng là
        // trạng thái StopSlaAsync dùng cho mọi luồng kết thúc khác (reject/merge/declare-incident),
        // và GetRemainingPercent trả 0 cho nó nên UI không vẽ thanh đếm ngược nữa.
        //
        // Paused cũng phải được dừng: một ticket đang tạm dừng vẫn có thể bị đóng do false alarm,
        // và nếu để nguyên thì timer treo vĩnh viễn ở Paused trên một ticket đã đóng.
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(t => t.TicketId == ticket.Id, ct);
        if (timer is not null
            && timer.Status is SlaTimerStatusEnum.Running or SlaTimerStatusEnum.Paused)
        {
            timer.Status = SlaTimerStatusEnum.Stopped;
            timer.CurrentPauseStartedAt = null;
            _uow.SlaTimers.UpdateAsync(timer);
        }

        await _activityLogger.LogAsync(
            ticket.Id,
            actorUserId: evt.ResolvedByUserId,
            ActorRoleEnum.System,
            actorDisplayName: "Environmental Incident",
            ActivityActionEnum.StatusChanged,
            oldValue: oldStatus.ToString(),
            newValue: TicketStatusEnum.ClosedRejected.ToString(),
            reason: $"Auto-close: incident {evt.IncidentId} false alarm. {evt.ResolutionNote}".Trim());

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Auto-close ticket {TicketId} do incident {IncidentId} false alarm.", ticket.Id, evt.IncidentId);
    }
}
