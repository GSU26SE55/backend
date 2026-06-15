using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint IoT-2 #IoT2-28 — quét reading mới insert mỗi 30s,
/// gọi <see cref="ICrossSourceValidationService"/> để phát hiện Bms vs IoT lệch ngưỡng.
/// </summary>
public class CrossSourceValidationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromSeconds(75); // overlap nhỏ để tránh miss.

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CrossSourceValidationBackgroundService> _logger;

    public CrossSourceValidationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CrossSourceValidationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 1 chu kỳ đầu để app fully boot.
        try
        { await Task.Delay(Interval, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICrossSourceValidationService>();
                var since = DateTime.UtcNow.Subtract(LookbackWindow);
                var created = await svc.ScanRecentReadingsAsync(since, stoppingToken);
                if (created > 0)
                    _logger.LogInformation("CrossSourceValidation created {Count} SensorMismatch alert(s)", created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CrossSourceValidation scan failed");
            }

            try
            { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
