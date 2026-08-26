using BatteryService.Application.Common.Models;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Tick định kỳ → ghi nhật ký bảo trì cho pin đã tới kỳ.
/// </summary>
/// <remarks>
/// Thay cho <c>PeriodicMaintenanceBackgroundService</c> bên TicketService: lịch bảo trì là
/// thuộc tính của tài sản nên nó thuộc về BatteryService. Worker này chỉ đọc một cột có
/// index (<c>next_maintenance_due_at_utc</c>) thay vì GroupBy toàn bảng ticket mỗi tick,
/// và không tạo ticket — chỉ ghi mốc theo dõi kèm SoH.
/// </remarks>
public class MaintenanceScheduleBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MaintenanceScheduleOptions> _options;
    private readonly ILogger<MaintenanceScheduleBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public MaintenanceScheduleBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MaintenanceScheduleOptions> options,
        ILogger<MaintenanceScheduleBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Maintenance schedule worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMaintenanceScheduleService>();
                var recorded = await service.RecordDueCyclesAsync(
                    _timeProvider.GetUtcNow().UtcDateTime, stoppingToken);

                if (recorded > 0)
                    _logger.LogInformation("Recorded {Count} maintenance cycle(s).", recorded);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Maintenance schedule tick failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds),
                stoppingToken);
        }
    }
}
