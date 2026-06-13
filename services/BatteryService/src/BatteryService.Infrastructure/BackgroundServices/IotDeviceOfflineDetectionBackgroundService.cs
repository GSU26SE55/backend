using BatteryService.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint IoT-2 #IoT2-11 (S2-BE-08) — tick 2 phút, gọi <see cref="IIotDeviceOfflineDetectionService"/>.
/// Threshold default 300s (5 phút) — config qua <c>Iot:OfflineAfterSeconds</c>.
/// Spec §52.6: chạy 2 phút/lần (120s default).
/// </summary>
public class IotDeviceOfflineDetectionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IotDeviceOfflineDetectionBackgroundService> _logger;

    public IotDeviceOfflineDetectionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IotDeviceOfflineDetectionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _configuration.GetValue("Iot:OfflineCheckIntervalSeconds", 120);
        var offlineAfterSeconds = _configuration.GetValue("Iot:OfflineAfterSeconds", 300);
        var batchSize = _configuration.GetValue("Iot:OfflineBatchSize", 100);

        _logger.LogInformation(
            "IotDeviceOfflineDetectionBackgroundService started (interval={Interval}s, offlineAfter={Threshold}s)",
            intervalSeconds, offlineAfterSeconds);

        var delay = TimeSpan.FromSeconds(Math.Max(15, intervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IIotDeviceOfflineDetectionService>();
                await service.DetectAsync(offlineAfterSeconds, batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IoT offline detection tick failed");
            }

            try
            { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("IotDeviceOfflineDetectionBackgroundService stopped");
    }
}
