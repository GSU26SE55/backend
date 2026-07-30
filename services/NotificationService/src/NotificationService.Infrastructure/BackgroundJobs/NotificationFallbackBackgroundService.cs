using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Sprint 6.3 NOTI3-05 (#705) — chuỗi dự phòng push → SMS cho notification critical.
///
/// **Vấn đề:** một cảnh báo P1 (pin mất điện, sự cố môi trường) gửi qua push mà thiết bị không nhận
/// được thì không ai biết — hệ thống vẫn ghi <c>Sent</c> và im lặng cho tới khi SLA 4h trôi qua.
///
/// **Cách chữa (nhánh B, chốt 30/07/2026):** dựa trên dữ liệu receipt của NOTI3-02. Push critical
/// đã gửi quá <c>PushReceiptTimeoutMinutes</c> mà KHÔNG có receipt <c>Ok</c> nào ⇒ sinh thêm một bản
/// SMS bù cho cùng người nhận, đánh dấu <c>PayloadJson.fallbackFrom</c> để báo cáo không đếm trùng
/// thành hai notification.
///
/// **Giới hạn phải nói rõ (R-44):** chuỗi này chỉ cứu ca *push hỏng*. Nếu chính gateway SMS chết
/// (một chiếc điện thoại Android — hết pin, mất mạng) thì không có đường nào khác, vì nhánh B đã
/// chốt KHÔNG mua provider thứ hai.
/// </summary>
public class NotificationFallbackBackgroundService : BackgroundService
{
    private const string LeaderKey = "notification_fallback_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    /// <summary>Khoá đánh dấu bản SMS bù, để không bù chồng và để báo cáo loại ra khi đếm.</summary>
    public const string FallbackFromKey = "fallbackFrom";

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly NotificationFallbackOptions _options;
    private readonly NotificationDispatchOptions _dispatchOptions;
    private readonly ExpoReceiptOptions _receiptOptions;
    private readonly ILogger<NotificationFallbackBackgroundService> _logger;

    public NotificationFallbackBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        IOptions<NotificationFallbackOptions> options,
        IOptions<NotificationDispatchOptions> dispatchOptions,
        ILogger<NotificationFallbackBackgroundService> logger,
        // Optional để test cũ không phải sửa; thiếu thì dùng mặc định của ExpoReceiptOptions.
        IOptions<ExpoReceiptOptions>? receiptOptions = null)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _dispatchOptions = dispatchOptions.Value;
        _receiptOptions = receiptOptions?.Value ?? new ExpoReceiptOptions();
        _logger = logger;
    }

    /// <summary>
    /// Ngưỡng chờ tối thiểu để fallback KHÔNG bắn trước khi worker đối soát kịp biết kết quả.
    ///
    /// Receipt chỉ được hỏi Expo từ <c>MinAgeMinutes</c> trở đi, mà worker quét theo chu kỳ nên
    /// xấu nhất lỡ trọn một nhịp; cộng thêm biên cho độ trễ HTTP. Đặt thấp hơn con số này thì
    /// **mọi** push critical đều lãnh một SMS thừa.
    /// </summary>
    private int MinimumSafeTimeoutMinutes =>
        Math.Max(0, _receiptOptions.MinAgeMinutes)
        + (int)Math.Ceiling(Math.Max(0, _receiptOptions.PollIntervalSeconds) / 60.0)
        + SafetyMarginMinutes;

    private const int SafetyMarginMinutes = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "NotificationFallback: TẮT theo cấu hình — push critical thất bại sẽ KHÔNG được bù bằng SMS "
                + "(Notification:Fallback:Enabled).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "NotificationFallback: bật (instance={Instance}, mỗi {Interval}s, ngưỡng chờ receipt {Timeout}').",
            _instanceId, interval.TotalSeconds, _options.PushReceiptTimeoutMinutes);

        // Cấu hình sai ở đây không gây lỗi kỹ thuật nào — chỉ âm thầm gửi SMS thừa cho mọi push
        // critical. Phải nói to lúc khởi động, không thì không ai phát hiện ra.
        if (_options.PushReceiptTimeoutMinutes < MinimumSafeTimeoutMinutes)
        {
            _logger.LogError(
                // Mỗi placeholder phải ứng với ĐÚNG một đối số: Microsoft.Extensions.Logging điền
                // theo VỊ TRÍ, nên lặp lại {Minimum} ở cuối câu sẽ ăn mất một slot không có đối số
                // (CA2017) và in ra "{Minimum}" nguyên văn. Câu chốt vì vậy diễn đạt lại, không lặp tên.
                "NotificationFallback: CẤU HÌNH SAI — Notification:Fallback:PushReceiptTimeoutMinutes = {Actual}' "
                + "nhỏ hơn ngưỡng an toàn {Minimum}' (= ExpoReceipt:MinAgeMinutes {MinAge}' + chu kỳ quét "
                + "{PollMinutes}' + biên {Margin}'). Fallback sẽ bắn SMS TRƯỚC KHI worker đối soát kịp biết "
                + "receipt, nghĩa là MỌI push critical đều lãnh thêm một SMS thừa. "
                + "Hãy nâng giá trị này lên tối thiểu bằng ngưỡng an toàn nêu trên.",
                _options.PushReceiptTimeoutMinutes,
                MinimumSafeTimeoutMinutes,
                _receiptOptions.MinAgeMinutes,
                (int)Math.Ceiling(_receiptOptions.PollIntervalSeconds / 60.0),
                SafetyMarginMinutes);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                if (await IsLeaderAsync(stoppingToken))
                    await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "NotificationFallback: vòng quét lỗi."); }
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
            // Redis lỗi ⇒ vẫn chạy. Cảnh báo critical đến hai lần vẫn hơn là không đến.
            _logger.LogWarning(ex, "NotificationFallback: leader-election lỗi — vẫn xử lý.");
            return true;
        }
    }

    /// <summary>Một vòng quét. Public để test gọi trực tiếp, không phải chờ timer.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var criticalTypes = _dispatchOptions.ResolveCriticalTypes().ToArray();
        if (criticalTypes.Length == 0)
            return 0;

        var now = DateTime.UtcNow;
        var deadline = now.AddMinutes(-Math.Max(1, _options.PushReceiptTimeoutMinutes));

        // Ứng viên: push critical đã bàn giao cho Expo (Sent) trước hạn chót mà chưa lên Delivered.
        // KHÔNG lấy Delivered/Read/Opened — đó là những ca đã chắc chắn tới nơi.
        var candidates = await db.Notifications
            .Where(n => !n.IsDeleted
                        && n.Channel == NotificationChannelEnum.Push
                        && n.Status == NotificationStatusEnum.Sent
                        && n.SentAt != null
                        && n.SentAt <= deadline
                        && criticalTypes.Contains(n.Type))
            .OrderBy(n => n.SentAt)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return 0;

        var created = 0;

        foreach (var push in candidates)
        {
            // Có receipt Ok ⇒ đã tới thiết bị. Nâng luôn lên Delivered để lần quét sau không xét lại
            // (worker đối soát có thể đã cập nhật receipt nhưng gặp record ở trạng thái khác).
            var deliveredReceipt = await db.PushReceipts
                .AnyAsync(r => !r.IsDeleted
                               && r.NotificationId == push.Id
                               && r.Status == PushReceiptStatusEnum.Ok, ct);

            if (deliveredReceipt)
            {
                push.Status = NotificationStatusEnum.Delivered;
                continue;
            }

            // Đã bù rồi thì thôi — chống gửi SMS lặp mỗi vòng quét.
            var alreadyFallenBack = await db.Notifications
                .AnyAsync(n => !n.IsDeleted
                               && n.Channel == NotificationChannelEnum.Sms
                               && n.UserId == push.UserId
                               && n.Type == push.Type
                               && n.EntityId == push.EntityId
                               && n.PayloadJson != null
                               && n.PayloadJson.Contains(push.Id.ToString()), ct);

            if (alreadyFallenBack)
                continue;

            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = push.UserId,
                Type = push.Type,
                Channel = NotificationChannelEnum.Sms,
                Status = NotificationStatusEnum.Pending,
                Title = push.Title,
                Body = push.Body,
                PayloadJson = BuildFallbackPayload(push),
                EntityType = push.EntityType,
                EntityId = push.EntityId,
                CreatedAt = now,
            });

            AppMetrics.NotificationFallbackTotal.WithLabels("push", "sms").Inc();
            created++;

            _logger.LogWarning(
                "NotificationFallback: push critical {NotificationId} ({Type}) không có receipt sau {Timeout}' "
                + "— bù bằng SMS cho user {UserId}.",
                push.Id, push.Type, _options.PushReceiptTimeoutMinutes, push.UserId);
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Giữ nguyên payload gốc (deep link vẫn dùng được) và gắn thêm <c>fallbackFrom</c>.
    /// Payload gốc hỏng/không phải object thì dựng payload mới thay vì làm hỏng cả bản SMS.
    /// </summary>
    private static string BuildFallbackPayload(Notification push)
    {
        JsonObject payload;

        try
        {
            payload = string.IsNullOrWhiteSpace(push.PayloadJson)
                ? new JsonObject()
                : JsonNode.Parse(push.PayloadJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            payload = new JsonObject();
        }

        payload[FallbackFromKey] = push.Id.ToString();
        payload["fallbackChannel"] = nameof(NotificationChannelEnum.Push);

        return payload.ToJsonString();
    }
}
