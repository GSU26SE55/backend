using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

public sealed class SlaDeadlineReconciler : ISlaDeadlineReconciler
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ISlaCalculator _slaCalculator;

    public SlaDeadlineReconciler(ITicketUnitOfWork unitOfWork, ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _slaCalculator = slaCalculator;
    }

    public async Task ReconcileActiveTimersAsync(CancellationToken cancellationToken = default)
    {
        var timers = await _unitOfWork.SlaTimers.GetAllAsync()
            .Where(x => !x.IsDeleted
                        && (x.Status == SlaTimerStatusEnum.Running || x.Status == SlaTimerStatusEnum.Paused))
            .ToListAsync(cancellationToken);

        var timerIds = timers.Select(x => x.Id).ToList();
        var completedPauseEvents = timerIds.Count == 0
            ? []
            : await _unitOfWork.SlaPauseEvents.GetAllAsync()
                .Where(x => !x.IsDeleted
                            && timerIds.Contains(x.SlaTimerId)
                            && x.ResumedAt.HasValue)
                .ToListAsync(cancellationToken);

        var ticketIds = timers.Select(x => x.TicketId).Distinct().ToList();
        var tickets = ticketIds.Count == 0
            ? new Dictionary<Guid, TicketStatusEnum>()
            : await _unitOfWork.Tickets.GetAllAsync()
                .Where(x => !x.IsDeleted && ticketIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);

        var pausedMinutesByTimer = new Dictionary<Guid, int>();
        foreach (var pauseEvent in completedPauseEvents)
        {
            var eligibleMinutes = Math.Max(0, (int)_slaCalculator.GetWorkingMinutesBetween(
                pauseEvent.PausedAt,
                pauseEvent.ResumedAt!.Value));

            pauseEvent.DurationMinutes = eligibleMinutes;
            pausedMinutesByTimer[pauseEvent.SlaTimerId] = checked(
                pausedMinutesByTimer.GetValueOrDefault(pauseEvent.SlaTimerId) + eligibleMinutes);
            _unitOfWork.SlaPauseEvents.UpdateAsync(pauseEvent);
        }

        foreach (var timer in timers)
        {
            var isStage1Open = tickets.TryGetValue(timer.TicketId, out var status) && status == TicketStatusEnum.Open;
            if (isStage1Open)
            {
                timer.TotalPausedMinutes = 0;
                timer.OriginalDueAt = _slaCalculator.CalculateResponseDueDate(timer.StartedAt, timer.Priority);
                timer.DueAt = timer.OriginalDueAt;
            }
            else
            {
                timer.TotalPausedMinutes = pausedMinutesByTimer.GetValueOrDefault(timer.Id);
                timer.OriginalDueAt = _slaCalculator.CalculateDueDate(timer.StartedAt, timer.Priority);
                timer.DueAt = _slaCalculator.AddWorkingMinutes(timer.OriginalDueAt, timer.TotalPausedMinutes);
            }
            _unitOfWork.SlaTimers.UpdateAsync(timer);
        }
    }
}
