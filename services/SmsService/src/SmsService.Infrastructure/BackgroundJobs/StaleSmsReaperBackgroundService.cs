using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.BackgroundJobs;

/// <summary>
/// Revert <c>Sending → Pending</c> khi device claim &gt; 5 phút mà chưa report.
/// KHÔNG bump <c>RetryCount</c> (không tính lần thất bại từ phía device).
/// Tick 1 phút.
/// </summary>
public class StaleSmsReaperBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleSmsReaperBackgroundService> _logger;

    public StaleSmsReaperBackgroundService(IServiceScopeFactory scopeFactory, ILogger<StaleSmsReaperBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        _logger.LogInformation("[SmsService] StaleSmsReaper started. Tick={Tick}, Threshold={Threshold}",
            TickInterval, StaleThreshold);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
                var threshold = DateTime.UtcNow - StaleThreshold;

                var stale = await db.SmsMessages
                    .Where(x => x.Status == SmsStatus.Sending
                                && x.PickedAt != null
                                && x.PickedAt < threshold
                                && !x.IsDeleted)
                    .ToListAsync(stoppingToken);

                if (stale.Count == 0)
                    continue;

                var now = DateTime.UtcNow;
                foreach (var m in stale)
                {
                    m.ReapStaleClaim(now);
                    db.SmsAuditLogs.Add(new SmsAuditLog
                    {
                        Id = Guid.NewGuid(),
                        SmsMessageId = m.Id,
                        Event = SmsAuditEvent.Reaped,
                        CreatedAt = now,
                        Detail = "Stale claim reaped after 5 minutes."
                    });
                }

                try
                {
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("[SmsService] StaleSmsReaper reverted {Count} stale SMS.", stale.Count);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // Một số row đã bị device khác claim/report cùng lúc — bỏ qua, tick sau xử lý.
                    _logger.LogWarning(ex, "[SmsService] StaleSmsReaper concurrency conflict; will retry next tick.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SmsService] StaleSmsReaper tick failed.");
            }
        }
    }
}
