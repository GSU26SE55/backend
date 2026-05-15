using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Tick định kỳ <c>ScanIntervalSeconds</c> → gọi <see cref="IAnomalyDetectionService"/>.
/// BackgroundService chỉ làm cron trigger; toàn bộ logic detect/dedup/persist nằm trong service.
/// </summary>
public class ThresholdCheckBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnomalyEngineOptions _options;
    private readonly ILogger<ThresholdCheckBackgroundService> _logger;

    public ThresholdCheckBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnomalyEngineOptions> options,
        ILogger<ThresholdCheckBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.ScanIntervalSeconds);
        // Overlap 2x để không miss reading; dedup ở service handle duplicates.
        var lookback = interval + interval;
        _logger.LogInformation(
            "ThresholdCheckBackgroundService started (interval={Interval}s)", _options.ScanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
                var result = await service.ScanRecentReadingsAsync(lookback, stoppingToken);

                if (result.AlertsCreated > 0 || result.AlertsMerged > 0)
                {
                    _logger.LogInformation(
                        "ThresholdCheck: scanned={Scanned}, created={Created}, merged={Merged}, outbox={Outbox}",
                        result.ReadingsScanned, result.AlertsCreated, result.AlertsMerged, result.OutboxEventsQueued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ThresholdCheck tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("ThresholdCheckBackgroundService stopped");
    }
}
