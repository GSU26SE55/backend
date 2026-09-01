using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

public class TicketActivationService : ITicketActivationService
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly ISlaCalculator _slaCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketActivationService(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        ISlaCalculator slaCalculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _slaCalculator = slaCalculator;
        _activityLogger = activityLogger;
        _outboxWriter = outboxWriter;
    }

    public async Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct)
    {
        var ticket = request.Ticket;
        var resumesHeldCycle = ticket.Status == TicketStatusEnum.Pending &&
                               ticket.PendingContext == PendingContextEnum.Held &&
                               request.Reason is ActivationReason.ScheduledDue or ActivationReason.EarlyResume;
        if (ticket.ScheduleVersion != request.ExpectedScheduleVersion)
            return new(false, "The schedule version is stale.");
        if (ticket.Status is not (TicketStatusEnum.Open or TicketStatusEnum.Pending or TicketStatusEnum.ReAssign))
            return new(false, $"Ticket status {ticket.Status} cannot be activated.");
        if (request.Reason == ActivationReason.ScheduledDue &&
            (!ticket.ScheduledStartAtUtc.HasValue || ticket.ScheduledStartAtUtc.Value > request.NowUtc))
            return new(false, "The scheduled start is not due.");
        if (request.Reason == ActivationReason.EarlyResume && ticket.PendingContext != PendingContextEnum.Held)
            return new(false, "Only a held ticket can be resumed early.");

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AccountId == request.PrimaryHandlerStaffId && !x.IsDeleted, ct);
        if (staff is null || staff.Status != AccountStatusEnum.Active || !staff.IsAvailable ||
            ticket.Priority is null || !AssignmentRoleHelper.ValidatePrimaryHandlerTier(ticket.Priority.Value, staff.SkillTier))
            return new(false, "The PrimaryHandler is no longer active, available, or tier-qualified.");

        var fromStatus = ticket.Status;  // capture BEFORE state machine changes ticket.Status
        ticket.PrimaryHandlerStaffId = request.PrimaryHandlerStaffId;
        var transition = await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
        {
            ActorUserId = request.ActorUserId,
            ActorRole = request.ActorRole,
            ActorDisplayName = request.ActorDisplayName
        }, ct);
        if (!transition.IsAllowed)
            return new(false, transition.Reason);

        await ApplySlaAsync(ticket, request.NowUtc, fromStatus, resumesHeldCycle, ct);
        await _activityLogger.LogAsync(ticket.Id, request.ActorUserId, request.ActorRole,
            request.ActorDisplayName, ActivityActionEnum.StatusChanged,
            oldValue: "Scheduled", newValue: nameof(TicketStatusEnum.InProgress),
            reason: request.UserReason ?? request.Reason.ToString());
        await _outboxWriter.WriteAsync(new TicketWorkStartedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, request.PrimaryHandlerStaffId,
            request.NowUtc, ticket.ScheduleVersion, request.Reason.ToString(),
            ticket.Priority?.ToString() ?? "Unknown", ticket.ScheduledStartAtUtc), ct);
        return new(true);
    }

    public async Task CompleteSlaAsync(Ticket ticket, CancellationToken ct)
    {
        // Mark the Resolution timer as Met when the ticket is resolved.
        // The Response timer was already settled (Met/Breached) when staff was first assigned.
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id
                                   && x.Type == SlaTimerTypeEnum.Resolution
                                   && !x.IsDeleted, ct);
        if (timer?.Status != SlaTimerStatusEnum.Running)
            return;

        timer.Status = SlaTimerStatusEnum.Met;
        timer.CurrentPauseStartedAt = null;
        _uow.SlaTimers.UpdateAsync(timer);
    }

    public Task StartCorrectionSlaAsync(Ticket ticket, DateTime nowUtc, CancellationToken ct)
        => ApplySlaAsync(ticket, nowUtc, ticket.Status, resumesHeldCycle: false, ct);

    public async Task StopSlaAsync(Ticket ticket, CancellationToken ct)
    {
        // Stop all active timers — covers both Response (Open tickets) and Resolution (InProgress).
        var timers = await _uow.SlaTimers.GetAllAsync()
            .Where(x => x.TicketId == ticket.Id
                     && !x.IsDeleted
                     && x.Status != SlaTimerStatusEnum.Stopped)
            .ToListAsync(ct);
        foreach (var timer in timers)
        {
            timer.Status = SlaTimerStatusEnum.Stopped;
            timer.CurrentPauseStartedAt = null;
            _uow.SlaTimers.UpdateAsync(timer);
        }
    }

    private async Task ApplySlaAsync(
        Ticket ticket,
        DateTime nowUtc,
        TicketStatusEnum fromStatus,
        bool resumesHeldCycle,
        CancellationToken ct)
    {
        // ApplySlaAsync operates on the Resolution stage — it is called when a ticket
        // moves to InProgress. The Response timer is never touched here.
        var resolutionTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id
                                   && x.Type == SlaTimerTypeEnum.Resolution
                                   && !x.IsDeleted, ct);

        // Urgent: stop any existing Resolution timer; no new timer is created.
        if (ticket.Priority == TicketPriorityEnum.Urgent)
        {
            if (resolutionTimer is not null)
            {
                resolutionTimer.Status = SlaTimerStatusEnum.Stopped;
                resolutionTimer.CurrentPauseStartedAt = null;
                _uow.SlaTimers.UpdateAsync(resolutionTimer);
            }
            return;
        }

        var priority = ticket.Priority!.Value;

        // Nhánh 1 — Resume a held-and-paused cycle (Pending/Held → InProgress)
        if (resumesHeldCycle &&
            resolutionTimer?.Status == SlaTimerStatusEnum.Paused && resolutionTimer.Priority == priority)
        {
            await ResumePausedTimerAsync(resolutionTimer, nowUtc, ct);
            return;
        }

        // Nhánh 2 — Post-breach reassignment (ReAssign or already InProgress via correction):
        // preserve the ENTIRE contractual clock — the escalation adds staff/tier, it does not
        // re-scope the SLA. Priority is deliberately NOT synced onto the timer either: the
        // deadline stays on the budget it was created with (SPE O&M v4.0 § Record Control), so
        // syncing Priority would leave GetRemainingPercent dividing the old DueAt by the new,
        // larger budget — the % collapses even though nothing about the clock changed. The
        // ticket's own Priority is the source of truth for "current priority" in the UI.
        if (fromStatus is TicketStatusEnum.ReAssign or TicketStatusEnum.InProgress)
        {
            if (resolutionTimer is null)
                return;  // no existing Resolution timer — nothing to preserve, skip
            if (resolutionTimer.Status != SlaTimerStatusEnum.Breached)
                resolutionTimer.Status = SlaTimerStatusEnum.Running;
            // Priority, DueAt, OriginalDueAt, BreachAt, TotalPausedMinutes, PauseEpisodesCount,
            // WarningSentAt are all intentionally left unchanged.
            _uow.SlaTimers.UpdateAsync(resolutionTimer);
            return;
        }

        // Settle Response timer if still Running: moving Open/Pending -> InProgress means first response is done
        var responseTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id
                                   && x.Type == SlaTimerTypeEnum.Response
                                   && !x.IsDeleted, ct);
        if (responseTimer is not null && responseTimer.Status == SlaTimerStatusEnum.Running)
        {
            responseTimer.Status = SlaTimerStatusEnum.Met;
            _uow.SlaTimers.UpdateAsync(responseTimer);
        }

        // Nhánh 3 — Fresh activation (Open/Pending → InProgress):
        // If a Resolution timer already exists for this ticket (e.g. from seeder or prior transition), update it.
        // Otherwise, insert a new Resolution timer.
        if (resolutionTimer is not null)
        {
            resolutionTimer.Priority = priority;
            if (resolutionTimer.Status != SlaTimerStatusEnum.Breached)
            {
                var effectiveStartedAt = _slaCalculator.NormalizeToNextWorkingInstant(nowUtc);
                var dueAt = _slaCalculator.CalculateDueDate(effectiveStartedAt, priority);
                resolutionTimer.StartedAt = effectiveStartedAt;
                resolutionTimer.DueAt = dueAt;
                resolutionTimer.OriginalDueAt = dueAt;
                resolutionTimer.Status = SlaTimerStatusEnum.Running;
            }
            _uow.SlaTimers.UpdateAsync(resolutionTimer);
            return;
        }

        var newStartedAt = _slaCalculator.NormalizeToNextWorkingInstant(nowUtc);
        var newDueAt = _slaCalculator.CalculateDueDate(newStartedAt, priority);
        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = SlaTimerTypeEnum.Resolution,
            Priority = priority,
            StartedAt = newStartedAt,
            DueAt = newDueAt,
            OriginalDueAt = newDueAt,
            Status = SlaTimerStatusEnum.Running
        });
    }

    private async Task ResumePausedTimerAsync(SlaTimer timer, DateTime nowUtc, CancellationToken ct)
    {
        var pause = await _uow.SlaPauseEvents.GetAllAsync()
            .Where(x => x.SlaTimerId == timer.Id && x.ResumedAt == null && !x.IsDeleted)
            .OrderByDescending(x => x.PausedAt)
            .FirstOrDefaultAsync(ct);
        if (pause is not null)
        {
            pause.ResumedAt = nowUtc;
            pause.DurationMinutes = Math.Max(0,
                (int)_slaCalculator.GetWorkingMinutesBetween(pause.PausedAt, nowUtc));
            timer.TotalPausedMinutes += pause.DurationMinutes.Value;
            timer.DueAt = _slaCalculator.AddWorkingMinutes(timer.DueAt, pause.DurationMinutes.Value);
        }
        timer.Status = SlaTimerStatusEnum.Running;
        timer.CurrentPauseStartedAt = null;
        _uow.SlaTimers.UpdateAsync(timer);
    }
}
