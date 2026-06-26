using AuditAggregatorService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuditAggregatorService.Infrastructure.BackgroundJobs;

/// <summary>
/// Retention cho read-store (Sprint audit #AUDIT-41). Daily ~03:00 UTC: xóa row <c>audit_aggregate</c> cũ hơn
/// 6 tháng TRỪ severity Critical/Security (giữ vĩnh viễn — D15). Source tables retain 1 năm (per-service job).
/// </summary>
public class AuditRetentionBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(180); // ~6 tháng

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditRetentionBackgroundService> _logger;

    public AuditRetentionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AuditRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        _logger.LogInformation("AuditRetention started (window={Days}d, except Critical/Security).", RetentionWindow.TotalDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                var now = DateTime.UtcNow;
                // Chỉ chạy quanh 03:00 UTC (CheckInterval 6h đảm bảo trúng cửa sổ này 1 lần/ngày).
                if (now.Hour is < 3 or > 4) continue;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IAuditAggregatorUnitOfWork>();
                var cutoff = now - RetentionWindow;

                // Bulk delete row cũ hơn cutoff TRỪ severity Critical/Security (giữ vĩnh viễn — D15).
                var deleted = await uow.AuditAggregates.GetAllAsync()
                    .Where(x => x.OccurredAt < cutoff && x.Severity != "Critical" && x.Severity != "Security")
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("AuditRetention xóa {Count} row cũ hơn {Cutoff:O}.", deleted, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AuditRetention tick failed."); }
        }
    }
}
