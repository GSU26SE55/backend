using System.Text;
using System.Text.Json;
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

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.2 NOTI-12 (#683) — gom notification không-critical thành 1 bản tin tổng hợp cho user
/// đã bật digest (<c>NotificationPreference.Frequency = Daily</c> hoặc <c>DigestWindowMinutes</c>).
///
/// Luồng: <c>NotificationDispatcher</c> gặp record Email/Push không-critical của user bật digest →
/// hoãn (đặt <c>NextAttemptAt = now + window</c>, KHÔNG tăng attempt). Worker này quét các record đã
/// tới hạn, gom theo (user × channel) thành 1 record tổng hợp mới rồi đánh dấu các record gốc là Sent
/// (chúng đã được giao — dưới dạng một mục trong digest).
///
/// Record tổng hợp mang <c>EntityType = NotificationDigest.EntityType</c> để dispatcher không gom
/// nó vào một digest khác.
///
/// Lưu ý: digest CHỈ áp cho Email/Push. Record InApp vẫn được gửi ngay nên lịch sử in-app của user
/// không bị thiếu mục nào.
/// </summary>
public class NotificationDigestBackgroundService : BackgroundService
{
    private const string LeaderKey = "notification_digest_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(2);

    private static readonly NotificationChannelEnum[] DigestChannels =
        [NotificationChannelEnum.Email, NotificationChannelEnum.Push];

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLease _lease;
    private readonly NotificationDigestOptions _options;
    private readonly ILogger<NotificationDigestBackgroundService> _logger;

    public NotificationDigestBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedLease lease,
        IOptions<NotificationDigestOptions> options,
        ILogger<NotificationDigestBackgroundService> logger)
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
            _logger.LogInformation("NotificationDigest bị tắt qua Notification:Digest:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.PollIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation("NotificationDigest started (instance={Instance}, interval={Interval}).",
            _instanceId, interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                if (await IsLeaderAsync(stoppingToken))
                    await BuildDigestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "NotificationDigest tick failed."); }
        }

        _logger.LogInformation("NotificationDigest stopped (instance={Instance}).", _instanceId);
    }

    /// <summary>
    /// GH-793 — giành quyền bằng MỘT lệnh nguyên tử có token chủ sở hữu.
    /// </summary>
    /// <remarks>
    /// Khuôn cũ <c>GET</c> rồi <c>SET</c> để lọt hai replica cùng đọc thấy khoá trống trong cùng một
    /// khoảnh khắc, và cả hai đều tự coi là chủ. <see cref="IDistributedLease"/> gộp kiểm-và-ghi vào
    /// một lệnh Redis nên khe hở đó biến mất.
    /// </remarks>
    private async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        try
        {
            return await _lease.TryAcquireAsync(LeaderKey, _instanceId, LeaseTtl, ct);
        }
        catch (Exception ex)
        {
            // Redis sự cố → vẫn chạy: không ai làm gì cả là hỏng nặng hơn làm trùng.
            _logger.LogWarning(ex, "Lease lỗi — chạy tiếp lượt này.");
            return true;
        }
    }

    /// <summary>Gom 1 vòng digest. Public để test gọi trực tiếp.</summary>
    public async Task<int> BuildDigestsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        // Record đã bị dispatcher hoãn để gom digest và nay tới hạn.
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatusEnum.Pending
                        && !n.IsDeleted
                        && n.NextAttemptAt != null
                        && n.NextAttemptAt <= now
                        && n.EntityType != NotificationDigest.EntityType
                        && DigestChannels.Contains(n.Channel))
            .OrderBy(n => n.CreatedAt)
            .Take(_options.BatchSize * 20)
            .ToListAsync(ct);

        if (due.Count == 0)
            return 0;

        // Chỉ gom cho user thực sự bật digest — record hoãn vì lý do khác (backoff lỗi, quiet hours)
        // phải để nguyên cho dispatch worker thử lại.
        var userIds = due.Select(n => n.UserId).Distinct().ToList();
        var prefs = await db.NotificationPreferences
            .Where(p => userIds.Contains(p.UserId) && !p.IsDeleted)
            .ToListAsync(ct);

        // Dùng CHUNG hàm quy đổi của dispatcher để hai bên không lệch định nghĩa "user có bật digest".
        var digestUsers = prefs
            .Where(p => Services.NotificationDispatcher.TryGetDigestWindow(p, out _))
            .Select(p => p.UserId)
            .ToHashSet();

        if (digestUsers.Count == 0)
            return 0;

        var groups = due
            .Where(n => digestUsers.Contains(n.UserId))
            .GroupBy(n => new { n.UserId, n.Channel })
            .Take(_options.BatchSize)
            .ToList();

        var created = 0;

        foreach (var group in groups)
        {
            if (ct.IsCancellationRequested)
                break;

            var items = group.OrderBy(n => n.CreatedAt).ToList();

            var aggregate = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = group.Key.UserId,
                Type = NotificationTypeEnum.System,
                Channel = group.Key.Channel,
                Status = NotificationStatusEnum.Pending,
                // Tiêu đề của mục con có thể dài đúng bằng trần 200 của cột, chép nguyên vào đây thì
                // vẫn vừa — nhưng cắt cho chắc, vì cột đích cũng chỉ 200.
                Title = Truncate(
                    items.Count == 1 ? items[0].Title : $"Bạn có {items.Count} thông báo mới",
                    TitleMaxLength),
                Body = BuildBody(items),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    digest = true,
                    count = items.Count,
                    from = items[0].CreatedAt,
                    to = items[^1].CreatedAt,
                    notificationIds = items.Select(i => i.Id).ToArray(),
                }),
                EntityType = NotificationDigest.EntityType,
                EntityId = null,
                NextAttemptAt = null,
            };

            await db.Notifications.AddAsync(aggregate, ct);

            foreach (var item in items)
            {
                // Các mục gốc coi như đã giao — nội dung của chúng nằm trong bản digest.
                item.Status = NotificationStatusEnum.Sent;
                item.SentAt = now;
                item.NextAttemptAt = null;
                item.FailureReason = null;
            }

            created++;
        }

        await db.SaveChangesAsync(ct);

        if (created > 0)
            _logger.LogInformation("NotificationDigest: tạo {Count} bản tin tổng hợp.", created);

        return created;
    }

    /// <summary>Khớp <c>NotificationConfiguration</c>: cột <c>title</c> 200, <c>body</c> 2000.</summary>
    private const int TitleMaxLength = 200;
    private const int BodyMaxLength = 2000;

    /// <summary>
    /// Gom nội dung các mục con thành thân bản tin tổng hợp.
    ///
    /// <para><b>Phải cắt.</b> Mỗi mục con có thân tối đa 2000 ký tự, mà <c>MaxItemsInBody</c> mặc
    /// định gom nhiều mục — chỉ cần hai mục dài là vượt luôn giới hạn 2000 của chính cột
    /// <c>body</c>, Postgres ném lỗi và cả vòng gom digest hỏng. Chưa nổ vì hệ thống chưa sinh bản
    /// tin gom nào, nhưng đó là bẫy chờ sẵn chứ không phải chuyện không xảy ra.</para>
    ///
    /// <para>Cắt ở ranh giới dòng chứ không cắt giữa câu: thà hiện ít mục mà đọc trọn còn hơn một
    /// mục cụt ngang. Phần bị bỏ luôn được nói ra bằng dòng "… và N thông báo khác" — người đọc
    /// biết còn thiếu để mở danh sách đầy đủ.</para>
    /// </summary>
    private string BuildBody(IReadOnlyList<Notification> items)
    {
        if (items.Count == 1)
            return Truncate(items[0].Body, BodyMaxLength);

        var shown = Math.Min(items.Count, Math.Max(1, _options.MaxItemsInBody));

        // Dựng dần và dừng ngay khi dòng kế tiếp sẽ làm vượt ngưỡng — cộng chỗ cho dòng kết.
        var sb = new StringBuilder();
        var used = 0;

        for (var i = 0; i < shown; i++)
        {
            var line = $"• {items[i].Title}: {items[i].Body}";
            var remaining = items.Count - i;
            var footer = $"… và {remaining} thông báo khác.";

            // Còn đủ chỗ cho dòng này VÀ cho dòng kết (nếu sau đó phải cắt) thì mới thêm.
            if (used + line.Length + 1 + footer.Length > BodyMaxLength)
            {
                sb.Append(footer);
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine(line);
            used = sb.Length;
        }

        if (items.Count > shown)
            sb.Append($"… và {items.Count - shown} thông báo khác.");

        // Chốt chặn cuối: dù mọi tính toán trên có sai thì cột vẫn không bao giờ bị tràn.
        return Truncate(sb.ToString().TrimEnd(), BodyMaxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
