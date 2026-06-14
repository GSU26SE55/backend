using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint IoT-2 #IoT2-38 — refresh gauge <c>iot_devices_online_count</c> mỗi 30s.
/// </summary>
public class DevicesOnlineGaugeBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevicesOnlineGaugeBackgroundService> _logger;

    public DevicesOnlineGaugeBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DevicesOnlineGaugeBackgroundService> logger)
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
                var metrics = scope.ServiceProvider.GetRequiredService<IIotMetricsRecorder>();

                var count = await uow.IotDevices.GetAllAsync()
                    .CountAsync(d => !d.IsDeleted && d.Status == IotDeviceStatusEnum.Active, stoppingToken);
                metrics.DevicesOnlineSet(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DevicesOnlineGauge refresh failed");
            }

            try
            { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
