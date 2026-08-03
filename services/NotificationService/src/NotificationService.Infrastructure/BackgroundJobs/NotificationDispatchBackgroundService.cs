using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Persistence;
using SharedInfrastructure.Metrics;

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.2 NOTI-01 (#672) — MẮT XÍCH CÒN THIẾU của notification pipeline.
///
/// Trước sprint này <c>NotificationDispatcher</c> đã viết xong và đăng ký DI nhưng KHÔNG có caller
/// nào; 22 consumer chỉ ghi record <c>Status=Pending</c> rồi thôi → Push/Email/SMS không bao giờ
/// được gửi ở runtime, user chỉ thấy notification khi tự poll REST (reviewnotification.md §3.1).
///
/// Worker này quét batch record <c>Pending</c> đến hạn và giao từng record xuống đúng channel của nó
/// (phương án (b) trong khuyến nghị §7.1: tách write/dispatch, retry độc lập).
///
/// Chống chạy trùng khi scale nhiều instance: Redis leader election, dùng lại đúng pattern của
/// <see cref="NotificationAuditOutboxRelayBackgroundService"/> (#AUDIT-34 / D12).
///
/// <para>
/// <b>⚠️ GIỚI HẠN ĐÃ BIẾT — trần thông lượng (Sprint 6.3 NOTI3-10 / #710, chốt nhánh B ngày
/// 30/07/2026).</b>
/// </para>
/// <para>
/// Leader election nghĩa là <b>chỉ MỘT instance xử lý</b> tại mỗi thời điểm, dù chạy bao nhiêu
/// replica. Trần thông lượng vì thế là:
/// </para>
/// <code>
/// tối đa ≈ BatchSize / PollIntervalSeconds
///        = 100 / 5 = 20 notification/giây  (giá trị mặc định)
/// </code>
/// <para>
/// Con số này thừa sức cho quy mô đồ án (vài chục pin, vài chục người dùng). Nó chỉ thành vấn đề khi
/// một sự kiện fan-out ra hàng nghìn người nhận cùng lúc — ví dụ sự cố toàn site khiến mọi khách hàng
/// đều phải được báo — hoặc khi số pin tăng vài bậc.
/// </para>
/// <para>
/// <b>Dấu hiệu phải nâng cấp:</b> theo dõi gauge <c>notification_pending_total</c> (NOTI3-07) và alert
/// <c>NotificationQueueBacklog</c>. Hàng đợi tồn đọng tăng đều mà không tự tiêu là lúc trần này bị
/// chạm thật.
/// </para>
/// <para>
/// <b>Cách nâng cấp khi tới lúc (nhánh A đã hoãn):</b> bỏ leader election, chia việc theo phân vùng —
/// hoặc theo <c>Channel</c>, hoặc theo <c>hash(UserId) % N</c> — kết hợp
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> để nhiều instance lấy các lô rời nhau mà không tranh chấp.
/// Ước tính ~1.5 ngày công. Hoãn lại vì tối ưu trước khi có số liệu là đoán mò; NOTI3-07 làm trước
/// chính là để có số liệu đó.
/// </para>
/// <para>
/// Ghi chú tương ứng ở <c>overall.md §3.4 — Giới hạn đã biết</c>.
/// </para>
/// </summary>
public class NotificationDispatchBackgroundService : BackgroundService
{
    private const string LeaderKey = "notification_dispatch_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly NotificationDispatchOptions _options;
    private readonly ILogger<NotificationDispatchBackgroundService> _logger;

    public NotificationDispatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        IOptions<NotificationDispatchOptions> options,
        ILogger<NotificationDispatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "NotificationDispatch bị tắt (Notification:Dispatch:Enabled=false) — notification sẽ nằm Pending, KHÔNG gửi đi.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "NotificationDispatch started (instance={Instance}, interval={Interval}s, batch={Batch}, maxAttempts={Max}).",
            _instanceId, _options.PollIntervalSeconds, _options.BatchSize, _options.MaxAttempts);

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

        _logger.LogInformation("NotificationDispatch stopped (instance={Instance}).", _instanceId);
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
            // Redis lỗi → vẫn xử lý (thà gửi trùng còn hơn không ai gửi).
            _logger.LogWarning(ex, "NotificationDispatch leader-election lỗi — fallback process.");
            return true;
        }
    }

    /// <summary>Quét + giao 1 batch. Public để integration/unit test gọi trực tiếp, không phải đợi timer.</summary>
    public async Task<DispatchBatchResult> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var now = DateTime.UtcNow;

        // NOTI3-07 (#707) — gauge queue lag. Đo TRƯỚC khi lấy batch để phản ánh tồn đọng thật,
        // và luôn ghi kể cả khi = 0 (nếu chỉ ghi khi > 0 thì gauge sẽ kẹt ở giá trị cũ).
        var totalDue = await db.Notifications
            .CountAsync(n => n.Status == NotificationStatusEnum.Pending
                             && !n.IsDeleted
                             && n.DispatchAttemptCount < _options.MaxAttempts
                             && (n.NextAttemptAt == null || n.NextAttemptAt <= now), ct);
        AppMetrics.NotificationPendingTotal.Set(totalDue);

        var pending = await db.Notifications
            .Where(n => n.Status == NotificationStatusEnum.Pending
                        && !n.IsDeleted
                        && n.DispatchAttemptCount < _options.MaxAttempts
                        && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .OrderBy(n => n.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        var result = new DispatchBatchResult();
        if (pending.Count == 0)
            return result;

        foreach (var notification in pending)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var outcome = await dispatcher.DispatchPendingAsync(notification, ct);
                switch (outcome)
                {
                    case DispatchOutcome.Sent:
                        result.Sent++;
                        break;
                    case DispatchOutcome.Retrying:
                        result.Retrying++;
                        break;
                    case DispatchOutcome.Failed:
                        result.Failed++;
                        break;
                    case DispatchOutcome.Deferred:
                        result.Deferred++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Không để 1 record hỏng chặn cả batch.
                result.Errored++;
                _logger.LogError(ex, "NotificationDispatch: lỗi khi gửi notification {Id}", notification.Id);
            }
        }

        if (result.Sent > 0 || result.Failed > 0 || result.Errored > 0)
        {
            _logger.LogInformation(
                "NotificationDispatch: sent={Sent}, retrying={Retrying}, failed={Failed}, deferred={Deferred}, errored={Errored}",
                result.Sent, result.Retrying, result.Failed, result.Deferred, result.Errored);
        }

        return result;
    }
}

/// <summary>Thống kê 1 vòng dispatch — phục vụ log + test.</summary>
public class DispatchBatchResult
{
    public int Sent { get; set; }
    public int Retrying { get; set; }
    public int Failed { get; set; }
    public int Deferred { get; set; }
    public int Errored { get; set; }

    public int Total => Sent + Retrying + Failed + Deferred + Errored;
}
