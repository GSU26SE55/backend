using BatteryService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 5B B1 (#152) — retention job xóa <c>NoiseBreachEvent</c> rows
/// > 7 ngày để hypertable không phình. Chạy mỗi 6 giờ.
/// </summary>
public class NoiseBreachRetentionBackgroundService : BackgroundService
{
    private static readonly TimeSpan RetentionDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoiseBreachRetentionBackgroundService> _logger;

    public NoiseBreachRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NoiseBreachRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NoiseBreachRetention started (retention=7d, tick=6h)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NoiseBreach retention tick failed");
            }

            try
            { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var cutoff = DateTime.UtcNow - RetentionDuration;

        // Sprint Bonus NS-10 (#654, N2) — breach đã promote thành Alert giữ vĩnh viễn để audit
        // (đúng doc-comment NoiseBreachEvent.PromotedToAlertId), chỉ purge breach chưa promote.
        var deleted = await uow.NoiseBreachEvents.GetAllAsync()
            .Where(n => n.Time < cutoff && n.PromotedToAlertId == null)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
            _logger.LogInformation("NoiseBreach retention purge: deleted {Count} rows older than {Cutoff:O}",
                deleted, cutoff);
    }
}
