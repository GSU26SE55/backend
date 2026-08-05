using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Interfaces;

namespace NotificationService.Infrastructure.Services;

/// <summary>
/// Sprint 6.3 NOTI3-06 (#706) — hạn mức bằng **cửa sổ trượt xấp xỉ** trên Redis.
///
/// **Vì sao xấp xỉ chứ không phải cửa sổ trượt tuyệt đối?**
/// Cửa sổ tuyệt đối phải lưu dấu thời gian của từng lần gửi (sorted set) — tốn bộ nhớ và cần nhiều
/// lệnh cho mỗi lần kiểm tra. Cách được dùng phổ biến trong công nghiệp (Cloudflare, Kong) là nội suy
/// hai cửa sổ cố định liền kề theo phần trăm thời gian đã trôi:
///
/// <code>
/// count ≈ đếm(cửa sổ trước) × (1 − phần đã trôi) + đếm(cửa sổ hiện tại)
/// </code>
///
/// Cách này chỉ cần một <c>INCR</c> atomic mỗi lần gửi, mà vẫn tránh được lỗ hổng kinh điển của cửa sổ
/// cố định: gửi hết hạn mức ở cuối giờ rồi lại gửi hết hạn mức ở đầu giờ sau (2× hạn mức trong vài phút).
///
/// **Fail-open có chủ đích:** Redis lỗi ⇒ cho gửi. Redis chết mà chặn hết notification thì một sự cố
/// hạ tầng phụ trợ sẽ làm câm luôn cả cảnh báo an toàn.
/// </summary>
public class NotificationRateLimiter : INotificationRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Giữ key thêm một cửa sổ vì phép nội suy còn cần đếm của cửa sổ trước.</summary>
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(2);

    private readonly ICacheService _cache;
    private readonly NotificationRateLimitOptions _options;
    private readonly ILogger<NotificationRateLimiter> _logger;

    public NotificationRateLimiter(
        ICacheService cache,
        IOptions<NotificationRateLimitOptions> options,
        ILogger<NotificationRateLimiter> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RateLimitDecision> TryConsumeAsync(
        Guid userId, NotificationTypeEnum type, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return RateLimitDecision.Allow;

        try
        {
            var now = DateTime.UtcNow;
            var currentBucket = now.Ticks / Window.Ticks;
            var elapsedFraction = (now.Ticks % Window.Ticks) / (double)Window.Ticks;

            var perHour = await EstimateAsync(
                $"noti_rl:{userId:N}:h:{currentBucket}",
                $"noti_rl:{userId:N}:h:{currentBucket - 1}",
                elapsedFraction, ct);

            if (_options.MaxPerUserPerHour > 0 && perHour > _options.MaxPerUserPerHour)
            {
                _logger.LogInformation(
                    "RateLimit: user {UserId} chạm trần {Max}/giờ (ước tính {Count:0.0}) — hoãn vào digest.",
                    userId, _options.MaxPerUserPerHour, perHour);
                return new RateLimitDecision(false, "per_hour");
            }

            var perType = await EstimateAsync(
                $"noti_rl:{userId:N}:t:{(int)type}:{currentBucket}",
                $"noti_rl:{userId:N}:t:{(int)type}:{currentBucket - 1}",
                elapsedFraction, ct);

            if (_options.MaxPerUserPerType > 0 && perType > _options.MaxPerUserPerType)
            {
                _logger.LogInformation(
                    "RateLimit: user {UserId} chạm trần {Max}/giờ cho loại {Type} (ước tính {Count:0.0}) — hoãn vào digest.",
                    userId, _options.MaxPerUserPerType, type, perType);
                return new RateLimitDecision(false, "per_type");
            }

            return RateLimitDecision.Allow;
        }
        catch (Exception ex)
        {
            // FAIL-OPEN: xem ghi chú ở tài liệu lớp.
            _logger.LogWarning(ex,
                "RateLimit: không kiểm tra được hạn mức cho user {UserId} — cho gửi (fail-open).", userId);
            return RateLimitDecision.Allow;
        }
    }

    /// <summary>Tăng bộ đếm cửa sổ hiện tại rồi nội suy với cửa sổ liền trước.</summary>
    private async Task<double> EstimateAsync(
        string currentKey, string previousKey, double elapsedFraction, CancellationToken ct)
    {
        var current = await _cache.IncrementAsync(currentKey, KeyTtl, ct);
        var previous = await _cache.GetCounterAsync(previousKey, ct) ?? 0;

        return previous * (1 - elapsedFraction) + current;
    }
}
