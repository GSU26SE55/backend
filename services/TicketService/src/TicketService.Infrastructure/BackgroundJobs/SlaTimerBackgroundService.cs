using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
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
        TimeProvider? timeProvider = null) // Inject TimeProvider
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking SLA violations.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        _logger.LogInformation("SLA Timer Background Service is stopping.");
    }

    protected async Task CheckSlaViolations(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checking for SLA violations...");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var currentTime = _timeProvider.GetUtcNow().UtcDateTime; // Dùng TimeProvider thay vì DateTime.UtcNow

        var runningTimers = await dbContext.SlaTimers
            .Include(st => st.Ticket)
            .Where(st => st.Status == SlaTimerStatusEnum.Running && !st.IsDeleted)
            .ToListAsync(stoppingToken);

        foreach (var timer in runningTimers)
        {
            var timeToBreach = timer.DueAt - currentTime;

            if (timeToBreach <= TimeSpan.Zero)
            {
                // SLA is breached
                _logger.LogWarning("SLA breached for ticket {TicketId}", timer.TicketId);
                timer.Status = SlaTimerStatusEnum.Breached;
                timer.BreachAt = currentTime;
                await bus.Publish(new SlaBreachedEvent { TicketId = timer.TicketId, BreachedAt = timer.BreachAt.Value }, stoppingToken);
            }
            else
            {
                var totalSlaDuration = timer.DueAt - timer.StartedAt;
                var elapsedTime = currentTime - timer.StartedAt;
                var percentage = (elapsedTime.TotalMinutes / totalSlaDuration.TotalMinutes) * 100;

                if (percentage >= 80 && timer.WarningSentAt == null)
                {
                    // SLA warning
                    _logger.LogWarning("SLA warning for ticket {TicketId}", timer.TicketId);
                    timer.WarningSentAt = currentTime;
                    await bus.Publish(new SlaWarningEvent { TicketId = timer.TicketId, WarningAt = timer.WarningSentAt.Value, Percentage = percentage }, stoppingToken);
                }
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }
}
