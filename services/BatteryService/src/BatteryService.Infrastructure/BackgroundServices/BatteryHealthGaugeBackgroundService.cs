using BatteryService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 7 #117 — refresh aggregate gauge sức khỏe pin mỗi 60s (cho dashboard Grafana "Battery Health").
///
/// Tính từ <b>latest reading mỗi asset</b> trong cửa sổ 24h (bounded để không full-scan hypertable):
/// SOH trung bình, số pin SOH &lt; 80%, DCIR trung bình, cell imbalance lớn nhất.
/// Detail per-asset thuộc web app qua Reports API #114.
/// </summary>
public class BatteryHealthGaugeBackgroundService : BackgroundService
{
    private const double SohThresholdPercent = 80.0;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatteryHealthGaugeBackgroundService> _logger;

    public BatteryHealthGaugeBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<BatteryHealthGaugeBackgroundService> logger)
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
                var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
                var metrics = scope.ServiceProvider.GetRequiredService<IBatteryHealthMetricsRecorder>();

                var since = DateTime.UtcNow - LookbackWindow;

                var recent = await uow.SensorReadings.GetAllAsync().AsNoTracking()
                    .Where(r => r.Time >= since)
                    .Select(r => new
                    {
                        r.BatteryAssetId,
                        r.Time,
                        r.SohPercent,
                        r.InternalResistanceMilliohm,
                        r.CellVoltageDeltaMv
                    })
                    .ToListAsync(stoppingToken);

                // Latest reading mỗi asset (in-memory — cửa sổ 24h bounded).
                var latestPerAsset = recent
                    .GroupBy(r => r.BatteryAssetId)
                    .Select(g => g.OrderByDescending(x => x.Time).First())
                    .ToList();

                var sohValues = latestPerAsset.Where(x => x.SohPercent.HasValue).Select(x => (double)x.SohPercent!.Value).ToList();
                var dcirValues = latestPerAsset.Where(x => x.InternalResistanceMilliohm.HasValue).Select(x => (double)x.InternalResistanceMilliohm!.Value).ToList();
                var imbalanceValues = latestPerAsset.Where(x => x.CellVoltageDeltaMv.HasValue).Select(x => (double)x.CellVoltageDeltaMv!.Value).ToList();

                var avgSoh = sohValues.Count > 0 ? sohValues.Average() : 0;
                var belowThreshold = sohValues.Count(v => v < SohThresholdPercent);
                var avgDcir = dcirValues.Count > 0 ? dcirValues.Average() : 0;
                var maxImbalance = imbalanceValues.Count > 0 ? imbalanceValues.Max() : 0;

                metrics.SetHealthGauges(avgSoh, belowThreshold, avgDcir, maxImbalance);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatteryHealthGauge refresh failed");
            }

            try
            { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
