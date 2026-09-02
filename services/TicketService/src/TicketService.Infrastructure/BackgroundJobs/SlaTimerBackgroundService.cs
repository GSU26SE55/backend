using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Utils;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.BackgroundJobs;

public class SlaTimerBackgroundService : BackgroundService
{
    private const double WarningThresholdPercent = 80d;

    private readonly ILogger<SlaTimerBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public SlaTimerBackgroundService(
        ILogger<SlaTimerBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSlaViolations(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "SLA timer tick failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async Task CheckSlaViolations(CancellationToken ct)
    {
        List<Guid> ids;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            ids = await db.SlaTimers.AsNoTracking()
                .Where(timer => !timer.IsDeleted
                                && !timer.Ticket.IsDeleted
                                && !TicketStatusGroups.Terminal.Contains(timer.Ticket.Status)
                                && timer.Ticket.Status != TicketStatusEnum.Completed
                                && timer.Ticket.Priority != TicketPriorityEnum.Urgent
                                && (timer.Status == SlaTimerStatusEnum.Running
                                    // Rescue window only applies to Resolution timers —
                                    // a Breached Response timer must not trigger rescue logic.
                                    || (timer.Status == SlaTimerStatusEnum.Breached
                                        && timer.Type == SlaTimerTypeEnum.Resolution
                                        && timer.Ticket.Status == TicketStatusEnum.InProgress)))
                .OrderBy(timer => timer.DueAt)
                .Select(timer => timer.Id)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
            await ProcessTimerAsync(id, ct);
    }

    private async Task ProcessTimerAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>();
        var slaCalculator = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? null
            : await db.Database.BeginTransactionAsync(ct);

        try
        {
            var timer = await db.SlaTimers
                .Include(x => x.Ticket)
                .ThenInclude(x => x.Assignments)
                .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            var isEligibleTimer = timer?.Status == SlaTimerStatusEnum.Running
                || (timer?.Status == SlaTimerStatusEnum.Breached
                    && timer.Ticket.Status == TicketStatusEnum.InProgress);
            if (timer is null
                || !isEligibleTimer
                || timer.Ticket.IsDeleted
                || TicketStatusGroups.Terminal.Contains(timer.Ticket.Status)
                || timer.Ticket.Status == TicketStatusEnum.Completed
                || timer.Ticket.Priority == TicketPriorityEnum.Urgent)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                return;
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

            // Rescue window: monitor 24h working-hour budget for post-breach reassigned Staff
            if (timer.Status == SlaTimerStatusEnum.Breached
                && timer.Ticket.Status == TicketStatusEnum.InProgress)
            {
                var currentAssignment = timer.Ticket.Assignments
                    .FirstOrDefault(a => a.Role == AssignmentRoleEnum.PrimaryHandler && !a.IsDeleted);
                var rescueExpired = false;
                if (currentAssignment is not null)
                {
                    var rescueMinutes = slaCalculator.GetWorkingMinutesBetween(
                        currentAssignment.CreatedAt, nowUtc);
                    if (rescueMinutes > 1440)
                    {
                        rescueExpired = true;
                        // Fire second SlaBreachedEvent → EscalationBackgroundService escalates P1→Urgent
                        // and fires BatteryIsolationRequestedEvent via the existing chain.
                        await outbox.WriteAsync(new SlaBreachedEvent
                        {
                            TicketId = timer.TicketId,
                            BreachedAt = nowUtc,
                            Priority = timer.Ticket.Priority?.ToString() ?? string.Empty,
                            Code = timer.Ticket.Code,
                            IsRescueWindowExpired = true
                        }, ct);
                        timer.Status = SlaTimerStatusEnum.Stopped;
                        timer.CurrentPauseStartedAt = null;
                    }
                }
                if (rescueExpired)
                {
                    await db.SaveChangesAsync(ct);
                    if (transaction is not null)
                        await transaction.CommitAsync(ct);
                }
                else if (transaction is not null)
                {
                    await transaction.RollbackAsync(ct);
                }
                return;
            }

            if (timer.DueAt <= nowUtc)
            {
                timer.Status = SlaTimerStatusEnum.Breached;
                timer.BreachAt = nowUtc;
                db.TicketActivities.Add(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = timer.TicketId,
                    ActorUserId = Guid.Empty,
                    ActorRole = ActorRoleEnum.System,
                    ActorDisplayName = "System",
                    Action = ActivityActionEnum.SlaBreached,
                    OldValue = timer.Priority.ToString(),
                    NewValue = SlaTimerStatusEnum.Breached.ToString(),
                    Reason = timer.Type == SlaTimerTypeEnum.Response
                        ? "Response SLA breached (initial response deadline exceeded)."
                        : "Resolution SLA breached (resolution deadline exceeded).",
                    Ticket = null!
                });

                await outbox.WriteAsync(new SlaBreachedEvent
                {
                    TicketId = timer.TicketId,
                    BreachedAt = nowUtc,
                    Priority = timer.Ticket.Priority?.ToString() ?? string.Empty,
                    Code = timer.Ticket.Code
                }, ct);
            }
            else if (timer.Ticket.Status == TicketStatusEnum.Open)
            {
                // Stage 1 (Open): 24/7 calendar clock. Warning sent at >=80% without IsWorkingTime gate. StaffId = null.
                var totalSeconds = (timer.DueAt - timer.StartedAt).TotalSeconds;
                var elapsedSeconds = (nowUtc - timer.StartedAt).TotalSeconds;
                var consumedPercent = totalSeconds > 0
                    ? Math.Clamp(elapsedSeconds / totalSeconds * 100d, 0d, 100d)
                    : 100d;

                if (timer.WarningSentAt is null && consumedPercent >= WarningThresholdPercent)
                {
                    timer.WarningSentAt = nowUtc;
                    await outbox.WriteAsync(new SlaWarningEvent
                    {
                        TicketId = timer.TicketId,
                        WarningAt = nowUtc,
                        Percentage = Math.Round(consumedPercent, 2, MidpointRounding.AwayFromZero),
                        Code = timer.Ticket.Code,
                        StaffId = null
                    }, ct);
                }
            }
            else
            {
                // Stage 2 (InProgress / Request / ReAssign / Pending): Working-hours clock
                var remainingPercent = slaCalculator.GetRemainingPercent(timer, nowUtc);
                var consumedPercent = 100d - remainingPercent;
                var shouldSendInitialWarning = timer.WarningSentAt is null
                                               && consumedPercent >= WarningThresholdPercent
                                               && slaCalculator.IsWorkingTime(nowUtc);
                var shouldSendReminder = timer.WarningSentAt.HasValue
                                         && slaCalculator.ShouldSendNextSessionReminder(
                                             timer.WarningSentAt.Value, nowUtc);

                if (shouldSendInitialWarning || shouldSendReminder)
                {
                    timer.WarningSentAt = nowUtc;
                    await outbox.WriteAsync(new SlaWarningEvent
                    {
                        TicketId = timer.TicketId,
                        WarningAt = nowUtc,
                        Percentage = Math.Round(consumedPercent, 2, MidpointRounding.AwayFromZero),
                        Code = timer.Ticket.Code,
                        StaffId = timer.Ticket.Assignments
                            .FirstOrDefault(x => !x.IsDeleted
                                                 && x.Role == AssignmentRoleEnum.PrimaryHandler)
                            ?.StaffId
                    }, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            _logger.LogInformation(exception, "SLA timer {TimerId} lost a concurrency race.", id);
        }
    }
}
