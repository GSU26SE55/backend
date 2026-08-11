using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Persistence;
using SharedInfrastructure.Leasing;
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

    /// <summary>GH-793 — cứ bấy nhiêu bản ghi thì gia hạn quyền một lần.</summary>
    private const int LeaseRenewEvery = 10;

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLease _lease;
    private readonly NotificationDispatchOptions _options;
    private readonly ILogger<NotificationDispatchBackgroundService> _logger;

    public NotificationDispatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedLease lease,
        IOptions<NotificationDispatchOptions> options,
        ILogger<NotificationDispatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _lease = lease;
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
                    await ProcessBatchAsync(stoppingToken, KeepLeaseAliveAsync);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "NotificationDispatch tick failed."); }
        }

        _logger.LogInformation("NotificationDispatch stopped (instance={Instance}).", _instanceId);
    }

    /// <summary>
    /// GH-793 — giành quyền chạy bằng một lệnh nguyên tử có token chủ sở hữu.
    /// </summary>
    /// <remarks>
    /// Khuôn cũ là <c>GET</c> rồi <c>SET</c>: hai replica cùng đọc thấy khoá trống trong cùng một
    /// khoảnh khắc thì cả hai đều tự coi là chủ. <see cref="IDistributedLease"/> gộp kiểm-và-ghi vào
    /// một lệnh Redis nên khe hở đó biến mất.
    /// <para>
    /// Redis sự cố thì vẫn chạy tiếp, y như trước — không gửi gì cả là hỏng nặng hơn gửi trùng.
    /// Khác biệt so với trước: bây giờ mất lease KHÔNG còn dẫn tới gửi trùng, vì mỗi bản ghi còn phải
    /// qua cửa <c>TryClaimForDispatchAsync</c> ở tầng cơ sở dữ liệu.
    /// </para>
    /// </remarks>
    private async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        try
        {
            return await _lease.TryAcquireAsync(LeaderKey, _instanceId, LeaseTtl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "NotificationDispatch lease lỗi — chạy tiếp; claim ở tầng DB vẫn chặn gửi trùng.");
            return true;
        }
    }

    /// <summary>
    /// Gia hạn quyền giữa chừng một lượt chạy dài.
    /// </summary>
    /// <remarks>
    /// Một batch có thể gọi hàng trăm lần ra ngoài (Expo, Mailjet, SMS gateway) và vượt quá thời hạn
    /// 30 giây. Không gia hạn thì instance khác giành được quyền trong khi lượt này vẫn đang chạy.
    /// Mất quyền thì dừng lượt lại — phần còn lại thuộc về chủ mới.
    /// </remarks>
    private async Task<bool> KeepLeaseAliveAsync(CancellationToken ct)
    {
        try
        {
            return await _lease.TryRenewAsync(LeaderKey, _instanceId, LeaseTtl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationDispatch gia hạn lease lỗi — chạy tiếp lượt này.");
            return true;
        }
    }

    /// <summary>Quét + giao 1 batch. Public để integration/unit test gọi trực tiếp, không phải đợi timer.</summary>
    /// <param name="keepLeaseAlive">
    /// GH-793 — hàm gia hạn quyền, gọi định kỳ giữa lượt; trả <c>false</c> nghĩa là đã mất quyền và
    /// lượt này phải dừng. Bỏ trống khi gọi từ test hoặc khi chạy một mình.
    /// </param>
    public async Task<DispatchBatchResult> ProcessBatchAsync(
        CancellationToken ct, Func<CancellationToken, Task<bool>>? keepLeaseAlive = null)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var now = DateTime.UtcNow;

        // GH-792 — thu hồi việc bị bỏ dở TRƯỚC khi đo và lấy batch.
        // Bản ghi đã chiếm (Processing) mà tiến trình chết giữa chừng sẽ nằm lại đó vĩnh viễn: nó
        // không còn khớp bộ lọc Pending nên không ai nhặt, và người dùng đơn giản là không nhận được
        // thông báo — không lỗi, không cảnh báo. Thu hồi ở đây chứ không ở job riêng để nó chạy dưới
        // cùng cơ chế chọn leader: hai instance cùng thu hồi thì lại sinh ra chính cái gửi trùng
        // mà cả thay đổi này đang tìm cách loại bỏ.
        var reclaimed = await ReclaimStaleClaimsAsync(db, now, ct);

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

        var result = new DispatchBatchResult { Reclaimed = reclaimed };
        if (pending.Count == 0)
            return result;

        var processed = 0;
        foreach (var notification in pending)
        {
            if (ct.IsCancellationRequested)
                break;

            // GH-793 — gia hạn quyền định kỳ giữa lượt chạy. Một batch 100 lần gọi ra ngoài thừa sức
            // vượt thời hạn 30 giây; lúc đó instance khác giành quyền và hai bên cùng chạy.
            // Kiểm mỗi 10 bản ghi thay vì mỗi bản: mỗi lần gia hạn là một vòng tới Redis, làm ở mọi
            // bước sẽ tự biến mình thành nguồn chậm.
            if (keepLeaseAlive is not null && processed > 0 && processed % LeaseRenewEvery == 0
                && !await keepLeaseAlive(ct))
            {
                _logger.LogWarning(
                    "NotificationDispatch: mất quyền giữa lượt sau {Count} bản ghi — dừng, nhường chủ mới.",
                    processed);
                break;
            }

            processed++;

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

    /// <summary>
    /// GH-792 — trả các bản ghi đã chiếm quá lâu về hàng đợi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bản ghi vào <c>Processing</c> ngay trước khi gọi provider. Nếu tiến trình chết trong lúc đó,
    /// nó nằm lại vĩnh viễn: không khớp bộ lọc <c>Pending</c> nên không vòng quét nào nhặt, và người
    /// dùng không nhận được thông báo mà chẳng có lỗi nào nổi lên.
    /// </para>
    /// <para>
    /// KHÔNG đụng tới <c>DispatchAttemptCount</c>: lần chiếm việc bị bỏ dở đã được tính từ lúc chiếm,
    /// nên một sự cố lặp lại vẫn tiến dần tới <c>MaxAttempts</c> thay vì quay vòng vô hạn.
    /// </para>
    /// <para>
    /// Lần gửi lại sau khi thu hồi có thể trùng với một lần gửi ĐÃ tới provider — đó là lý do ID
    /// message được sinh theo <c>NotificationId</c>, để phía nhận nhận ra và bỏ qua.
    /// </para>
    /// </remarks>
    private async Task<int> ReclaimStaleClaimsAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddSeconds(-Math.Max(30, _options.ProcessingTimeoutSeconds));

        var stale = await db.Notifications
            .Where(n => n.Status == NotificationStatusEnum.Processing
                        && !n.IsDeleted
                        && (n.ProcessingStartedAt == null || n.ProcessingStartedAt <= cutoff))
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return 0;

        foreach (var notification in stale)
        {
            notification.Status = NotificationStatusEnum.Pending;
            notification.ProcessingStartedAt = null;
            notification.FailureReason = "Reclaimed stalled work: the process stopped while sending.";
            notification.NextAttemptAt = now;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "NotificationDispatch: thu hồi {Count} bản ghi kẹt ở Processing quá {Seconds}s.",
            stale.Count, _options.ProcessingTimeoutSeconds);

        return stale.Count;
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

    /// <summary>GH-792 — số bản ghi kẹt ở <c>Processing</c> đã được trả về hàng đợi ở vòng này.</summary>
    public int Reclaimed { get; set; }

    public int Total => Sent + Retrying + Failed + Deferred + Errored;
}
