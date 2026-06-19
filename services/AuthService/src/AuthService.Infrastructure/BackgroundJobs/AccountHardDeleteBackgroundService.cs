using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// #AUTH-42: Hard-delete account đã soft-delete &gt; 90 ngày. Hỗ trợ window reactivate 90 ngày
/// (xem #AUTH-50), sau đó user mất hẳn quyền restore.
///
/// Cascade chính qua FK đã anonymize ở <see cref="AuthService.Application.CQRS.Handler.Account.DeleteAccountCommandHandler"/>
/// (#AUTH-30). Sau hard-delete row Account biến mất, các bảng phụ FK CASCADE xoá theo.
///
/// Daily 03:00 UTC.
/// </summary>
public class AccountHardDeleteBackgroundService : BackgroundService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountHardDeleteBackgroundService> _logger;

    public AccountHardDeleteBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AccountHardDeleteBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AccountHardDelete started. Retention={Days}d.", RetentionPeriod.TotalDays);

        var initialDelay = ComputeInitialDelay(DateTime.UtcNow);
        try
        { await Task.Delay(initialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            { await HardDeleteAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AccountHardDelete tick failed."); }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("AccountHardDelete stopped.");
    }

    private async Task HardDeleteAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = DateTime.UtcNow - RetentionPeriod;

        // IgnoreQueryFilters() để bypass global filter !IsDeleted (xem AccountConfiguration:156).
        // Chỉ hard-delete row đã soft-delete (DeletedAt != null) qua window.
        var affected = await db.Users
            .IgnoreQueryFilters()
            .Where(a => a.IsDeleted
                        && a.DeletedAt.HasValue
                        && a.DeletedAt.Value <= cutoff)
            .ExecuteDeleteAsync(ct);

        if (affected > 0)
            _logger.LogInformation("AccountHardDelete removed {Count} expired soft-deleted account(s).", affected);
    }

    private static TimeSpan ComputeInitialDelay(DateTime nowUtc)
    {
        var target = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 3, 0, 0, DateTimeKind.Utc);
        if (target <= nowUtc)
            target = target.AddDays(1);
        return target - nowUtc;
    }
}
