using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

public class SlaService : ISlaService
{
    private readonly ITicketUnitOfWork _uow;

    public SlaService(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task PauseSlaAsync(Guid ticketId, PauseReasonEnum reason, string? note, Guid userId, CancellationToken ct)
    {
        if (await IsClosedOrMergedAsync(ticketId, ct))
            return;
        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId && st.Status == SlaTimerStatusEnum.Running && !st.IsDeleted, ct);

        if (slaTimer == null)
            return;

        slaTimer.Status = SlaTimerStatusEnum.Paused;
        slaTimer.CurrentPauseStartedAt = DateTime.UtcNow;
        slaTimer.PauseEpisodesCount++;

        var pauseEvent = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = slaTimer.Id,
            Reason = reason,
            Note = note,
            PausedAt = DateTime.UtcNow,
            PausedByUserId = userId
        };
        await _uow.SlaPauseEvents.AddAsync(pauseEvent);
    }

    public async Task ResumeSlaAsync(Guid ticketId, Guid userId, CancellationToken ct)
    {
        if (await IsClosedOrMergedAsync(ticketId, ct))
            return;
        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId && st.Status == SlaTimerStatusEnum.Paused && !st.IsDeleted, ct);

        if (slaTimer == null)
            return;

        var pauseEvent = await _uow.SlaPauseEvents.GetAllAsync()
            .Where(pe => pe.SlaTimerId == slaTimer.Id && pe.ResumedAt == null && !pe.IsDeleted)
            .OrderByDescending(pe => pe.PausedAt)
            .FirstOrDefaultAsync(ct);

        if (pauseEvent != null)
        {
            pauseEvent.ResumedAt = DateTime.UtcNow;
            pauseEvent.ResumedByUserId = userId;

            var duration = (int)(pauseEvent.ResumedAt.Value - pauseEvent.PausedAt).TotalMinutes;
            pauseEvent.DurationMinutes = duration;

            slaTimer.TotalPausedMinutes += duration;
            slaTimer.DueAt = slaTimer.DueAt.AddMinutes(duration);
        }

        slaTimer.Status = SlaTimerStatusEnum.Running;
        slaTimer.CurrentPauseStartedAt = null;
    }

    public async Task PauseForCustomerInfoAsync(Guid ticketId, Guid chatId, Guid userId, CancellationToken ct)
        => await PauseSlaAsync(ticketId, PauseReasonEnum.AwaitingCustomerChat,
               $"Auto-paused: Staff requested customer info via chat {chatId}", userId, ct);

    public async Task ResumeOnCustomerReplyAsync(Guid ticketId, Guid userId, CancellationToken ct)
    {
        if (await IsClosedOrMergedAsync(ticketId, ct))
            return;
        var slaTimer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(st => st.TicketId == ticketId && st.Status == SlaTimerStatusEnum.Paused && !st.IsDeleted, ct);

        if (slaTimer == null)
            return;

        var openPause = await _uow.SlaPauseEvents.GetAllAsync()
            .Where(pe => pe.SlaTimerId == slaTimer.Id && pe.ResumedAt == null && !pe.IsDeleted)
            .OrderByDescending(pe => pe.PausedAt)
            .FirstOrDefaultAsync(ct);

        if (openPause == null || openPause.Reason != PauseReasonEnum.AwaitingCustomerChat)
            return;

        openPause.ResumedAt = DateTime.UtcNow;
        openPause.ResumedByUserId = userId;

        var duration = (int)(openPause.ResumedAt.Value - openPause.PausedAt).TotalMinutes;
        openPause.DurationMinutes = duration;

        slaTimer.TotalPausedMinutes += duration;
        slaTimer.DueAt = slaTimer.DueAt.AddMinutes(duration);
        slaTimer.Status = SlaTimerStatusEnum.Running;
        slaTimer.CurrentPauseStartedAt = null;
    }

    private Task<bool> IsClosedOrMergedAsync(Guid ticketId, CancellationToken ct) =>
        _uow.Tickets.GetAllAsync().AnyAsync(t => t.Id == ticketId && !t.IsDeleted &&
            (t.Status == TicketStatusEnum.Closed || t.CloseReason == TicketCloseReasonEnum.MergedDuplicate), ct);
}
