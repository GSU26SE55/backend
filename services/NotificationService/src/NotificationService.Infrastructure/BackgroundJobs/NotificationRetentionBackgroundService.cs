using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-11 (#711) — dọn notification quá hạn lưu trữ.
///
/// **Vấn đề:** bảng <c>notifications</c> chỉ tăng. Mỗi sự kiện fan-out ra tối đa 4 dòng (in-app,
/// push, email, sms); sau vài tháng vận hành là hàng triệu dòng, và chính truy vấn feed của người
/// dùng bị chậm theo. Không ai đọc lại notification từ nửa năm trước.
///
/// **Quy tắc dọn:**
/// <list type="bullet">
/// <item>Chỉ động vào notification đã ở trạng thái kết thúc (<c>Sent</c>/<c>Delivered</c>/<c>Read</c>/
/// <c>Opened</c>/<c>Failed</c>). Bản <c>Pending</c> tuyệt đối giữ lại — xoá là mất thông báo chưa
/// từng được gửi.</item>
/// <item>Notification thuộc <c>CriticalTypes</c> giữ VĨNH VIỄN: đó là bằng chứng "đã cảnh báo",
/// cần cho điều tra sự cố và đối chiếu SLA.</item>
/// <item><b>Xoá mềm</b> (<c>IsDeleted</c>) chứ không <c>DELETE</c> thật — dữ liệu vẫn phục hồi được
/// nếu ngưỡng cấu hình sai, và tránh khoá bảng lâu.</item>
/// </list>
///
/// Chạy hằng đêm vào giờ thấp điểm; chống chạy trùng nhiều instance bằng leader election Redis
/// (cùng khuôn với <see cref="NotificationDispatchBackgroundService"/>).
/// </summary>
public class NotificationRetentionBackgroundService : BackgroundService
{
    private const string LeaderKey = "notification_retention_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(10);

    /// <summary>Nhịp kiểm tra "đã tới giờ chạy chưa". Không phải nhịp dọn.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly NotificationRetentionOptions _options;
    private readonly NotificationDispatchOptions _dispatchOptions;
    private readonly ILogger<NotificationRetentionBackgroundService> _logger;

    private DateOnly? _lastRunDate;

    public NotificationRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        IOptions<NotificationRetentionOptions> options,
        IOptions<NotificationDispatchOptions> dispatchOptions,
        ILogger<NotificationRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _dispatchOptions = dispatchOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "NotificationRetention: TẮT theo cấu hình — bảng notifications sẽ tăng vô hạn "
                + "(Notification:Retention:Enabled).");
            return;
        }

        _logger.LogInformation(
            "NotificationRetention: bật — giữ {Days} ngày, chạy lúc {Hour}h UTC, batch {Batch}.",
            _options.Days, _options.RunAtUtcHour, _options.BatchSize);

        using var timer = new PeriodicTimer(TickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            // Chỉ chạy một lần mỗi ngày, sau giờ đã hẹn.
            if (_lastRunDate == today || now.Hour < _options.RunAtUtcHour)
                continue;

            try
            {
                if (!await IsLeaderAsync(stoppingToken))
                    continue;

                var removed = await PurgeAsync(stoppingToken);
                _lastRunDate = today;

                _logger.LogInformation("NotificationRetention: đã dọn {Count} notification quá hạn.", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "NotificationRetention: vòng dọn lỗi."); }
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
            // Redis lỗi ⇒ KHÔNG dọn. Khác với dispatcher: ở đây "làm trùng" nghĩa là hai instance
            // cùng xoá — thà hoãn tới đêm sau còn hơn dọn nhầm khi không chắc.
            _logger.LogWarning(ex, "NotificationRetention: leader-election lỗi — bỏ qua lượt dọn này.");
            return false;
        }
    }

    /// <summary>Dọn một lượt. Public để test gọi trực tiếp, không phải chờ tới giờ hẹn.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.Days));
        var batchSize = Math.Clamp(_options.BatchSize, 1, 5000);
        var maxBatches = Math.Clamp(_options.MaxBatchesPerRun, 1, 1000);

        var criticalTypes = _options.KeepCriticalForever
            ? _dispatchOptions.ResolveCriticalTypes().ToArray()
            : [];

        var total = 0;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            if (ct.IsCancellationRequested)
                break;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var query = db.Notifications
                .Where(n => !n.IsDeleted
                            && n.CreatedAt < cutoff
                            // Pending KHÔNG bao giờ bị dọn — xoá là mất thông báo chưa từng gửi.
                            && n.Status != NotificationStatusEnum.Pending);

            if (criticalTypes.Length > 0)
                query = query.Where(n => !criticalTypes.Contains(n.Type));

            var stale = await query
                .OrderBy(n => n.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);

            if (stale.Count == 0)
                break;

            var now = DateTime.UtcNow;
            foreach (var notification in stale)
            {
                notification.IsDeleted = true;
                notification.DeletedAt = now;
            }

            await db.SaveChangesAsync(ct);
            total += stale.Count;

            // Batch chưa đầy ⇒ đã hết dữ liệu quá hạn, không cần vòng nữa.
            if (stale.Count < batchSize)
                break;

            if (batch == maxBatches - 1)
            {
                _logger.LogWarning(
                    "NotificationRetention: chạm trần {Max} batch trong một lượt — còn tồn đọng, "
                    + "sẽ dọn tiếp đêm sau. Cân nhắc tăng BatchSize nếu lặp lại.",
                    maxBatches);
            }
        }

        return total;
    }
}
