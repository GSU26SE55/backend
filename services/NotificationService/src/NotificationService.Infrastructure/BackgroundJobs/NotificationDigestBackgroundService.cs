using System.Text;
using System.Text.Json;
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
    private readonly IDistributedCache _cache;
    private readonly NotificationDigestOptions _options;
    private readonly ILogger<NotificationDigestBackgroundService> _logger;

    public NotificationDigestBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        IOptions<NotificationDigestOptions> options,
        ILogger<NotificationDigestBackgroundService> logger)
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
            _logger.LogWarning(ex, "NotificationDigest leader-election lỗi — fallback process.");
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
                Title = items.Count == 1
                    ? items[0].Title
                    : $"Bạn có {items.Count} thông báo mới",
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

    private string BuildBody(IReadOnlyList<Notification> items)
    {
        if (items.Count == 1)
            return items[0].Body;

        var sb = new StringBuilder();
        var shown = Math.Min(items.Count, Math.Max(1, _options.MaxItemsInBody));

        for (var i = 0; i < shown; i++)
            sb.AppendLine($"• {items[i].Title}: {items[i].Body}");

        if (items.Count > shown)
            sb.AppendLine($"… và {items.Count - shown} thông báo khác.");

        return sb.ToString().TrimEnd();
    }
}
