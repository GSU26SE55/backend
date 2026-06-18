using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// #AUTH-49: clear OTP scratch fields (OtpCode/OtpExpiredAt/OtpPurpose) cho row có OtpExpiredAt qua
/// 24h. Tránh DB tích tụ rác OTP cũ (forensic + free Memory cache).
///
/// Daily 02:30 UTC (offset 30p so với PendingEmailCleanup để tránh contention).
/// </summary>
public class ExpiredOtpCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan CleanupGracePeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredOtpCleanupBackgroundService> _logger;

    public ExpiredOtpCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredOtpCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpiredOtpCleanup started. GracePeriod={Hours}h.", CleanupGracePeriod.TotalHours);

        var initialDelay = ComputeInitialDelay(DateTime.UtcNow);
        try
        { await Task.Delay(initialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            { await CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "ExpiredOtpCleanup tick failed."); }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("ExpiredOtpCleanup stopped.");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = DateTime.UtcNow - CleanupGracePeriod;

        // KHÔNG cleanup OTP của OtpPurpose=EmailChange (đã có PendingEmailCleanupBackgroundService riêng
        // xử lý cùng PendingEmail). Tránh double-cleanup gây race.
        var affected = await db.Users
            .Where(a => a.OtpCode != null
                        && a.OtpExpiredAt.HasValue
                        && a.OtpExpiredAt.Value < cutoff
                        && a.OtpPurpose != OtpPurposeEnum.EmailChange
                        && !a.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.OtpCode, (string?)null)
                .SetProperty(a => a.OtpExpiredAt, (DateTime?)null)
                .SetProperty(a => a.OtpPurpose, (OtpPurposeEnum?)null),
                ct);

        if (affected > 0)
            _logger.LogInformation("ExpiredOtpCleanup removed {Count} stale OTP record(s).", affected);
    }

    private static TimeSpan ComputeInitialDelay(DateTime nowUtc)
    {
        var target = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 2, 30, 0, DateTimeKind.Utc);
        if (target <= nowUtc)
            target = target.AddDays(1);
        return target - nowUtc;
    }
}
