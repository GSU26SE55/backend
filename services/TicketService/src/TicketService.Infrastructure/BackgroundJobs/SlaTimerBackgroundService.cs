using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.BackgroundJobs;

public class SlaTimerBackgroundService : BackgroundService
{
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
        _logger.LogInformation("SLA Timer Background Service is starting.");

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
                _logger.LogError(exception, "An error occurred while checking SLA violations.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        _logger.LogInformation("SLA Timer Background Service is stopping.");
    }

    public async Task CheckSlaViolations(CancellationToken stoppingToken)
    {
        List<Guid> timerIds;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            timerIds = await dbContext.SlaTimers
                .AsNoTracking()
                .Where(timer => timer.Status == SlaTimerStatusEnum.Running && !timer.IsDeleted)
                .OrderBy(timer => timer.DueAt)
                .Select(timer => timer.Id)
                .ToListAsync(stoppingToken);
        }

        foreach (var timerId in timerIds)
        {
            await ProcessTimerAsync(timerId, stoppingToken);
        }
    }

    private async Task ProcessTimerAsync(Guid timerId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>();
        await using var transaction = dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? null
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var timer = await dbContext.SlaTimers
                .Include(value => value.Ticket)
                .SingleOrDefaultAsync(value => value.Id == timerId && !value.IsDeleted, cancellationToken);

            if (timer is null
                || timer.Status != SlaTimerStatusEnum.Running
                || timer.Ticket.IsDeleted
                || timer.Ticket.Status == TicketStatusEnum.Closed
                || timer.Ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                return;
            }

            var currentTime = _timeProvider.GetUtcNow().UtcDateTime;
            var timeToBreach = timer.DueAt - currentTime;
            if (timeToBreach <= TimeSpan.Zero)
            {
                timer.Status = SlaTimerStatusEnum.Breached;
                timer.BreachAt = currentTime;
                await producer.WriteAsync(new SlaBreachedEvent
                {
                    TicketId = timer.TicketId,
                    BreachedAt = currentTime,
                    Priority = timer.Ticket.Priority?.ToString() ?? string.Empty
                }, cancellationToken);
            }
            else
            {
                var totalSlaDuration = timer.DueAt - timer.StartedAt;
                var elapsedTime = currentTime - timer.StartedAt;
                var percentage = totalSlaDuration.TotalMinutes <= 0
                    ? 100
                    : elapsedTime.TotalMinutes / totalSlaDuration.TotalMinutes * 100;

                if (percentage >= 80 && timer.WarningSentAt is null)
                {
                    timer.WarningSentAt = currentTime;
                    await producer.WriteAsync(new SlaWarningEvent
                    {
                        TicketId = timer.TicketId,
                        WarningAt = timer.WarningSentAt.Value,
                        Percentage = percentage,
                        StaffId = timer.Ticket?.Assignments.FirstOrDefault(a => a.Role == AssignmentRoleEnum.PrimaryHandler)?.StaffId
                    }, cancellationToken);
                }
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            _logger.LogInformation(exception, "SLA timer {TimerId} changed concurrently; skipped until next poll.", timerId);
        }
    }
}
