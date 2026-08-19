using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

public class SlaService : ISlaService
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ISlaCalculator _slaCalculator;
    private readonly TimeProvider _timeProvider;

    public SlaService(
        ITicketUnitOfWork uow,
        ISlaCalculator slaCalculator,
        TimeProvider? timeProvider = null)
    {
        _uow = uow;
        _slaCalculator = slaCalculator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SlaPauseEligibility> CheckPauseEligibilityAsync(Guid ticketId, CancellationToken ct)
    {
        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId
                                       && st.Status == SlaTimerStatusEnum.Running
                                       && !st.IsDeleted, ct);
        if (slaTimer == null)
            return new SlaPauseEligibility(true);

        var maxEpisodes = slaTimer.MaxPauseEpisodes > 0 ? slaTimer.MaxPauseEpisodes : 3;
        if (slaTimer.PauseEpisodesCount >= maxEpisodes)
            return new SlaPauseEligibility(false,
                $"Ticket reached the maximum pause limit ({maxEpisodes} time(s)).");

        var maxMinutes = slaTimer.MaxTotalPauseMinutes > 0 ? slaTimer.MaxTotalPauseMinutes : 2880;
        if (slaTimer.TotalPausedMinutes >= maxMinutes)
            return new SlaPauseEligibility(false, "The ticket's total pause duration reached the maximum limit.");

        return new SlaPauseEligibility(true);
    }

    public async Task PauseSlaAsync(
        Guid ticketId,
        PauseReasonEnum reason,
        string? note,
        Guid userId,
        CancellationToken ct)
    {
        if (await IsClosedOrMergedAsync(ticketId, ct))
            return;

        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId
                                       && st.Status == SlaTimerStatusEnum.Running
                                       && !st.IsDeleted, ct);
        if (slaTimer == null)
            return;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        slaTimer.Status = SlaTimerStatusEnum.Paused;
        slaTimer.CurrentPauseStartedAt = nowUtc;
        slaTimer.PauseEpisodesCount++;

        await _uow.SlaPauseEvents.AddAsync(new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = slaTimer.Id,
            PausedAt = nowUtc,
            PausedByUserId = userId,
            Reason = reason,
            Note = note
        });
        _uow.SlaTimers.UpdateAsync(slaTimer);
    }

    public async Task ResumeSlaAsync(Guid ticketId, Guid userId, CancellationToken ct)
    {
        if (await IsClosedOrMergedAsync(ticketId, ct))
            return;

        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId
                                       && st.Status == SlaTimerStatusEnum.Paused
                                       && !st.IsDeleted, ct);
        if (slaTimer == null)
            return;

        var pauseEvent = await _uow.SlaPauseEvents.GetAllAsync()
            .Where(pe => pe.SlaTimerId == slaTimer.Id && pe.ResumedAt == null && !pe.IsDeleted)
            .OrderByDescending(pe => pe.PausedAt)
            .FirstOrDefaultAsync(ct);
        if (pauseEvent == null)
            return;

        var resumedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var workingPauseMinutes = Math.Max(0,
            (int)_slaCalculator.GetWorkingMinutesBetween(pauseEvent.PausedAt, resumedAt));

        pauseEvent.ResumedAt = resumedAt;
        pauseEvent.ResumedByUserId = userId;
        pauseEvent.DurationMinutes = workingPauseMinutes;
        slaTimer.TotalPausedMinutes += workingPauseMinutes;
        slaTimer.DueAt = _slaCalculator.AddWorkingMinutes(slaTimer.DueAt, workingPauseMinutes);
        slaTimer.Status = SlaTimerStatusEnum.Running;
        slaTimer.CurrentPauseStartedAt = null;

        _uow.SlaPauseEvents.UpdateAsync(pauseEvent);
        _uow.SlaTimers.UpdateAsync(slaTimer);
    }

    public Task PauseForCustomerInfoAsync(Guid ticketId, Guid chatId, Guid userId, CancellationToken ct)
        => Task.CompletedTask;

    public Task ResumeOnCustomerReplyAsync(Guid ticketId, Guid userId, CancellationToken ct)
        => Task.CompletedTask;

    private Task<bool> IsClosedOrMergedAsync(Guid ticketId, CancellationToken ct)
        => _uow.Tickets.GetAllAsync().AnyAsync(
            t => t.Id == ticketId
                 && !t.IsDeleted
                 && (t.Status == TicketStatusEnum.Closed
                     || t.CloseReason == TicketCloseReasonEnum.MergedDuplicate),
            ct);
}
