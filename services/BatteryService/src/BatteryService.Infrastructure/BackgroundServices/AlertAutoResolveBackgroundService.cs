using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 5B B10 (#158) — tick định kỳ <c>AutoResolveIntervalSeconds</c>
/// → gọi <see cref="IAlertAutoResolveService"/> để auto-resolve Open alerts khi
/// anomaly không còn xuất hiện trong cửa sổ <c>AutoResolveLookbackMinutes</c>.
/// </summary>
public class AlertAutoResolveBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnomalyEngineOptions _options;
    private readonly ILogger<AlertAutoResolveBackgroundService> _logger;

    public AlertAutoResolveBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnomalyEngineOptions> options,
        ILogger<AlertAutoResolveBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.AutoResolveIntervalSeconds);
        _logger.LogInformation(
            "AlertAutoResolveBackgroundService started (interval={Interval}s, lookback={Lookback}min)",
            _options.AutoResolveIntervalSeconds, _options.AutoResolveLookbackMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAlertAutoResolveService>();
                var result = await service.AutoResolveAsync(
                    _options.AutoResolveLookbackMinutes,
                    _options.AutoResolveBatchSize, stoppingToken);

                if (result.Resolved > 0)
                {
                    _logger.LogInformation(
                        "AutoResolved {Resolved}/{Scanned} alert(s)",
                        result.Resolved, result.Scanned);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertAutoResolve tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("AlertAutoResolveBackgroundService stopped");
    }
}
