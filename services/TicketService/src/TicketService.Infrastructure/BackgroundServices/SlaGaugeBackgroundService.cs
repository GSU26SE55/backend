using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 7 #117 — refresh aggregate SLA gauge mỗi 60s (cho Grafana "SLA Ops").
/// compliance ratio = Met / (Met + Breached); breached count theo priority.
/// </summary>
public class SlaGaugeBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TicketPriorityEnum[] Priorities =
        { TicketPriorityEnum.P1Critical, TicketPriorityEnum.P2High, TicketPriorityEnum.P3Normal };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaGaugeBackgroundService> _logger;

    public SlaGaugeBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SlaGaugeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
                var metrics = scope.ServiceProvider.GetRequiredService<ISlaMetricsRecorder>();

                var timers = await uow.SlaTimers.GetAllAsync().AsNoTracking()
                    .Where(s => !s.IsDeleted)
                    .Select(s => new { s.Priority, s.Status })
                    .ToListAsync(stoppingToken);

                var met = timers.Count(t => t.Status == SlaTimerStatusEnum.Met);
                var breached = timers.Count(t => t.Status == SlaTimerStatusEnum.Breached);
                var finalized = met + breached;
                var complianceRatio = finalized == 0 ? 1.0 : Math.Round((double)met / finalized, 4);

                var breachedByPriority = Priorities.ToDictionary(
                    p => p.ToString(),
                    p => timers.Count(t => t.Priority == p && t.Status == SlaTimerStatusEnum.Breached));

                metrics.SetSlaGauges(complianceRatio, breachedByPriority);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SlaGauge refresh failed");
            }

            try
            { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
