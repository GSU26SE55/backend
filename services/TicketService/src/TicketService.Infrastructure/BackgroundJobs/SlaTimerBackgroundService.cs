using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Utils;
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

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
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
                                && timer.Status == SlaTimerStatusEnum.Running
                                && !timer.Ticket.IsDeleted
                                && timer.Ticket.Status == TicketStatusEnum.InProgress
                                && timer.Ticket.Priority != TicketPriorityEnum.Urgent)
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
            if (timer is null
                || timer.Status != SlaTimerStatusEnum.Running
                || timer.Ticket.IsDeleted
                || timer.Ticket.Status != TicketStatusEnum.InProgress
                || timer.Ticket.Priority == TicketPriorityEnum.Urgent)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                return;
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (timer.DueAt <= nowUtc)
            {
                timer.Status = SlaTimerStatusEnum.Breached;
                timer.BreachAt = nowUtc;
                await outbox.WriteAsync(new SlaBreachedEvent
                {
                    TicketId = timer.TicketId,
                    BreachedAt = nowUtc,
                    Priority = timer.Ticket.Priority?.ToString() ?? string.Empty,
                    Code = timer.Ticket.Code
                }, ct);
            }
            else
            {
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
                        Percentage = consumedPercent,
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
