using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// GH-672 NOTI-01 — worker quét Notification có Status=Pending rồi gửi qua channel tương ứng.
/// Trước ticket này INotificationDispatcher không có caller nào ở runtime nên Push/Email/Sms
/// chưa bao giờ thực sự được gửi. Redis leader election (D12) — pattern giống
/// <see cref="NotificationAuditOutboxRelayBackgroundService"/>.
/// </summary>
public class NotificationDispatchBackgroundService : BackgroundService
{
    private const int PollIntervalSeconds = 5;
    private const int BatchSize = 100;
    private const string LeaderKey = "notification_dispatch_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<NotificationDispatchBackgroundService> _logger;

    public NotificationDispatchBackgroundService(IServiceScopeFactory scopeFactory, IDistributedCache cache,
        ILogger<NotificationDispatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollIntervalSeconds));
        _logger.LogInformation("NotificationDispatch started (instance={Instance}).", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                if (await IsLeaderAsync(stoppingToken))
                    await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "NotificationDispatch tick failed."); }
        }
    }

    private async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        try
        {
            var current = await _cache.GetStringAsync(LeaderKey, ct);
            if (current is null || current == _instanceId)
            {
                await _cache.SetStringAsync(LeaderKey, _instanceId,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LeaseTtl }, ct);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationDispatch leader-election lỗi — fallback process.");
            return true;
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<INotificationUnitOfWork>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // Cả batch dùng chung 1 DbContext — cần để dọn ChangeTracker khi 1 row lỗi (xem catch bên dưới).
        // GetService (không phải GetRequiredService): unit test chỉ đăng ký mock UnitOfWork, không có DbContext.
        var dbContext = scope.ServiceProvider.GetService<DbContext>();

        // Tracking = true — dispatcher update Status/SentAt trên chính các entity này.
        //
        // Loại Email khỏi query: #673 chưa merge nên DispatchPendingAsync luôn hoãn row Email
        // (giữ Pending). Chúng là row cũ nhất nên nếu để lọt vào OrderBy(CreatedAt).Take(100),
        // chỉ cần tích đủ BatchSize row Email là batch bị chiếm chỗ hoàn toàn và các row
        // Push/InApp/Sms mới không bao giờ tới lượt. GỠ điều kiện này cùng lúc với nhánh skip
        // Email trong NotificationDispatcher.DispatchPendingAsync khi #673 land.
        var pending = await unitOfWork.Notifications.GetAllAsync()
            .Where(n => !n.IsDeleted
                && n.Status == NotificationStatusEnum.Pending
                && n.Channel != NotificationChannelEnum.Email)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var notification in pending)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                await dispatcher.DispatchPendingAsync(notification, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // 1 record lỗi không được chặn cả batch. Nếu lỗi xảy ra ở SaveChangesAsync thì entity vẫn
                // nằm trong ChangeTracker ở state Modified — save của row kế tiếp sẽ flush lại nó và fail
                // theo, kéo sập phần còn lại của batch. Detach đúng row lỗi (không Clear cả tracker, vì
                // InAppChannel.GetByIdAsync cần các row còn lại giữ nguyên identity đang track).
                if (dbContext is not null)
                    dbContext.Entry(notification).State = EntityState.Detached;
                _logger.LogWarning(ex, "NotificationDispatch: dispatch notification {NotificationId} thất bại.", notification.Id);
            }
        }
    }
}
