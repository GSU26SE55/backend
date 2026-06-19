using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// #AUTH-31: clear <c>Account.PendingEmail</c> nếu user khởi flow change-email nhưng không verify OTP
/// trong vòng 24h. Tránh PendingEmail "treo" mãi gây confusion + chiếm slot email mới (nếu future
/// có constraint check duplicate PendingEmail).
///
/// Trigger: <c>OtpPurpose = EmailChange</c> + <c>OtpExpiredAt &lt; now - 24h</c>.
/// (Không có field <c>PendingEmailRequestedAt</c> riêng, dùng OtpExpiredAt làm proxy — OTP gen ra
/// với TTL 5 phút (#AUTH-14), nên cleanup chỉ xảy ra 24h SAU khi OTP đã expire ~ 24h5p sau lúc request.)
///
/// Daily 02:00 UTC. Honor CancellationToken (graceful shutdown).
/// </summary>
public class PendingEmailCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan CleanupGracePeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingEmailCleanupBackgroundService> _logger;

    public PendingEmailCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingEmailCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PendingEmailCleanup started. GracePeriod={Hours}h, PollInterval={Hours}h.",
            CleanupGracePeriod.TotalHours, PollInterval.TotalHours);

        // Delay first run đến gần 02:00 UTC tiếp theo (sấp xỉ — đúng cho prod, test fixture sẽ override).
        var initialDelay = ComputeInitialDelay(DateTime.UtcNow);
        try
        { await Task.Delay(initialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingEmailCleanup tick failed; will retry next interval.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("PendingEmailCleanup stopped.");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = DateTime.UtcNow - CleanupGracePeriod;

        // Set-based UPDATE — nhanh hơn load + iterate. Chỉ clear PendingEmail + OTP scratch fields,
        // KHÔNG xóa account hay đổi status.
        var affected = await db.Users
            .Where(a => a.PendingEmail != null
                        && a.OtpPurpose == OtpPurposeEnum.EmailChange
                        && a.OtpExpiredAt.HasValue
                        && a.OtpExpiredAt.Value < cutoff
                        && !a.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.PendingEmail, (string?)null)
                .SetProperty(a => a.OtpCode, (string?)null)
                .SetProperty(a => a.OtpExpiredAt, (DateTime?)null)
                .SetProperty(a => a.OtpPurpose, (OtpPurposeEnum?)null),
                ct);

        if (affected > 0)
            _logger.LogInformation("PendingEmailCleanup removed {Count} stale pending email(s).", affected);
    }

    /// <summary>Initial delay đến 02:00 UTC kế tiếp. Nếu hiện tại đã sau 02:00 → đợi đến 02:00 ngày mai.</summary>
    private static TimeSpan ComputeInitialDelay(DateTime nowUtc)
    {
        var target = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 2, 0, 0, DateTimeKind.Utc);
        if (target <= nowUtc)
            target = target.AddDays(1);
        return target - nowUtc;
    }
}
