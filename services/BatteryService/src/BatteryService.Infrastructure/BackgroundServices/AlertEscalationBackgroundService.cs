using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Tick định kỳ <c>EscalationIntervalSeconds</c> → gọi <see cref="IAlertEscalationService"/>.
/// </summary>
public class AlertEscalationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnomalyEngineOptions _options;
    private readonly ILogger<AlertEscalationBackgroundService> _logger;

    public AlertEscalationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnomalyEngineOptions> options,
        ILogger<AlertEscalationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.EscalationIntervalSeconds);
        _logger.LogInformation(
            "AlertEscalationBackgroundService started (interval={Interval}s, escalateAfter={After}min)",
            _options.EscalationIntervalSeconds, _options.EscalationAfterMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAlertEscalationService>();
                var result = await service.EscalateAsync(
                    _options.EscalationAfterMinutes, batchSize: 100, stoppingToken);

                if (result.Escalated > 0)
                {
                    _logger.LogWarning("Escalated {Count} alert(s) chưa ack quá {Min}min",
                        result.Escalated, _options.EscalationAfterMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertEscalation tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("AlertEscalationBackgroundService stopped");
    }
}
