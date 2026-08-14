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

        ticket.PrimaryHandlerStaffId = request.PrimaryHandlerStaffId;
        var transition = await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
        {
            ActorUserId = request.ActorUserId,
            ActorRole = request.ActorRole,
            ActorDisplayName = request.ActorDisplayName
        }, ct);
        if (!transition.IsAllowed)
            return new(false, transition.Reason);

        await ApplySlaAsync(ticket, request.NowUtc, resumesHeldCycle, ct);
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
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, ct);
        if (timer?.Status != SlaTimerStatusEnum.Running)
            return;

        timer.Status = SlaTimerStatusEnum.Met;
        timer.CurrentPauseStartedAt = null;
        _uow.SlaTimers.UpdateAsync(timer);
    }

    public Task StartCorrectionSlaAsync(Ticket ticket, DateTime nowUtc, CancellationToken ct)
        => ApplySlaAsync(ticket, nowUtc, resumesHeldCycle: false, ct);

    public async Task StopSlaAsync(Ticket ticket, CancellationToken ct)
    {
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, ct);
        if (timer is null)
            return;

        timer.Status = SlaTimerStatusEnum.Stopped;
        timer.CurrentPauseStartedAt = null;
        _uow.SlaTimers.UpdateAsync(timer);
    }

    private async Task ApplySlaAsync(Ticket ticket, DateTime nowUtc, bool resumesHeldCycle, CancellationToken ct)
    {
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, ct);
        if (ticket.Priority == TicketPriorityEnum.Urgent)
        {
            if (timer is not null)
            {
                timer.Status = SlaTimerStatusEnum.Stopped;
                timer.CurrentPauseStartedAt = null;
                _uow.SlaTimers.UpdateAsync(timer);
            }
            return;
        }

        var priority = ticket.Priority!.Value;
        if (resumesHeldCycle &&
            timer?.Status == SlaTimerStatusEnum.Paused && timer.Priority == priority)
        {
            await ResumePausedTimerAsync(timer!, nowUtc, ct);
            return;
        }

        var dueAt = _slaCalculator.CalculateDueDate(nowUtc, priority);
        if (timer is null)
        {
            await _uow.SlaTimers.AddAsync(new SlaTimer
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Priority = priority,
                StartedAt = nowUtc,
                DueAt = dueAt,
                OriginalDueAt = dueAt,
                Status = SlaTimerStatusEnum.Running
            });
            return;
        }

        timer.Priority = priority;
        timer.StartedAt = nowUtc;
        timer.DueAt = dueAt;
        timer.OriginalDueAt = dueAt;
        timer.TotalPausedMinutes = 0;
        timer.CurrentPauseStartedAt = null;
        timer.WarningSentAt = null;
        timer.BreachAt = null;
        timer.Status = SlaTimerStatusEnum.Running;
        timer.PauseEpisodesCount = 0;
        _uow.SlaTimers.UpdateAsync(timer);
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
            pause.DurationMinutes = Math.Max(0, (int)(nowUtc - pause.PausedAt).TotalMinutes);
            timer.TotalPausedMinutes += pause.DurationMinutes.Value;
            timer.DueAt = timer.DueAt.AddMinutes(pause.DurationMinutes.Value);
        }
        timer.Status = SlaTimerStatusEnum.Running;
        timer.CurrentPauseStartedAt = null;
        _uow.SlaTimers.UpdateAsync(timer);
    }
}
