using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 7 B4 (§31.7) — tick định kỳ <c>CascadeRiskIntervalSeconds</c> (mặc định 5 phút) →
/// gọi <see cref="ICascadeRiskService"/> recompute cascade risk cho asset có Open alert.
/// </summary>
public class CascadeRiskBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnomalyEngineOptions _options;
    private readonly ILogger<CascadeRiskBackgroundService> _logger;

    public CascadeRiskBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnomalyEngineOptions> options,
        ILogger<CascadeRiskBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.CascadeRiskIntervalSeconds);
        _logger.LogInformation(
            "CascadeRiskBackgroundService started (interval={Interval}s)",
            _options.CascadeRiskIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICascadeRiskService>();
                var result = await service.RecomputeAsync(batchSize: 200, stoppingToken);

                if (result.HighRisk > 0 || result.MediumRisk > 0)
                {
                    _logger.LogWarning(
                        "Cascade risk scan: {Scanned} asset(s), {High} HIGH (>=0.7), {Medium} MEDIUM (>=0.5)",
                        result.Scanned, result.HighRisk, result.MediumRisk);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CascadeRisk tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }
}
