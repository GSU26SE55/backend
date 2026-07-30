using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;

namespace NotificationService.Infrastructure.Services;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly INotificationRateLimiter? _rateLimiter;         // Sprint 6.3 NOTI3-06 (#706)
    private readonly NotificationRateLimitOptions _rateLimitOptions; // Sprint 6.3 NOTI3-06 (#706)
    private readonly NotificationDispatchOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    private readonly IReadOnlyDictionary<NotificationTypeEnum, NotificationChannelEnum[]> _typeChannelMatrix;
    private readonly IReadOnlySet<NotificationTypeEnum> _criticalTypes;

    public NotificationDispatcher(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        IEnumerable<INotificationChannel> channels,
        ITemplateRenderer templateRenderer,
        INotificationAuditWriter auditWriter,
        IOptions<NotificationDispatchOptions> options,
        ILogger<NotificationDispatcher> logger,
        // Sprint 6.3 NOTI3-06 (#706) — optional để mọi caller/test cũ vẫn biên dịch nguyên vẹn.
        // Không truyền = không áp hạn mức (đúng hành vi trước sprint này).
        INotificationRateLimiter? rateLimiter = null,
        IOptions<NotificationRateLimitOptions>? rateLimitOptions = null)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _channels = channels;
        _templateRenderer = templateRenderer;
        _auditWriter = auditWriter;
        _rateLimiter = rateLimiter;
        _rateLimitOptions = rateLimitOptions?.Value ?? new NotificationRateLimitOptions();
        _options = options.Value;
        _logger = logger;

        // NOTI-14 (#685) — ma trận + critical types nay đọc từ config (default = giá trị hardcode cũ).
        _typeChannelMatrix = _options.ResolveTypeChannelMatrix();
        _criticalTypes = _options.ResolveCriticalTypes();
    }

    // ─────────────────────────── Fan-out từ event (tự tạo record) ───────────────────────────

    public async Task DispatchAsync(DispatchRequest request, CancellationToken ct = default)
    {
        if (request.Recipients.Count == 0)
            return;

        var targetChannels = _typeChannelMatrix.TryGetValue(request.Type, out var configured)
            ? configured
            : NotificationDispatchOptions.DefaultChannels;

        bool isCritical = request.BypassQuietHours || _criticalTypes.Contains(request.Type);

        foreach (var recipient in request.Recipients)
        {
            var pref = await LoadPreferenceAsync(recipient.UserId, ct);
            bool isQuiet = !isCritical && IsQuietHours(pref);

            // Push: 1 record per device token
            if (targetChannels.Contains(NotificationChannelEnum.Push))
                await DispatchPushAsync(request, recipient, pref, isQuiet, ct);

            // Non-push channels: 1 record each
            foreach (var channelType in targetChannels.Where(c => c != NotificationChannelEnum.Push))
            {
                // NOTI3-04 (#704) — xét cả tuỳ chọn theo nhóm nghiệp vụ, không chỉ công tắc kênh.
                if (!await IsChannelEnabledAsync(pref, request.Type, channelType, ct))
                    continue;
                if (isQuiet && channelType != NotificationChannelEnum.InApp)
                    continue;

                if (channelType == NotificationChannelEnum.Email && string.IsNullOrEmpty(recipient.Email))
                {
                    _logger.LogDebug("Dispatcher: no email for user {UserId}, skipping Email", recipient.UserId);
                    continue;
                }

                if (channelType == NotificationChannelEnum.Sms && string.IsNullOrEmpty(recipient.PhoneNumber))
                {
                    _logger.LogDebug("Dispatcher: no phone for user {UserId}, skipping Sms", recipient.UserId);
                    continue;
                }

                var channel = _channels.FirstOrDefault(c => c.ChannelType == channelType);
                if (channel is null)
                    continue;

                await SendSingleAsync(request, recipient, channelType, channel, isCritical, ct: ct);
            }
        }
    }

    private async Task DispatchPushAsync(
        DispatchRequest request,
        RecipientInfo recipient,
        NotificationPreference pref,
        bool isQuiet,
        CancellationToken ct)
    {
        // NOTI3-04 (#704) — push cũng phải qua cả hai cấp tuỳ chọn, không chỉ công tắc PushEnabled.
        if (isQuiet || !await IsChannelEnabledAsync(pref, request.Type, NotificationChannelEnum.Push, ct))
            return;

        var pushChannel = _channels.FirstOrDefault(c => c.ChannelType == NotificationChannelEnum.Push);
        if (pushChannel is null)
            return;

        var tokens = await _unitOfWork.DeviceTokens.GetAllAsync()
            .Where(dt => dt.UserId == recipient.UserId && !dt.IsDeleted && dt.IsActive)
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            _logger.LogDebug("Dispatcher: no device tokens for user {UserId}, skipping Push", recipient.UserId);
            return;
        }

        bool isCritical = _criticalTypes.Contains(request.Type) || request.BypassQuietHours;

        // NOTI-16 (#687) — 1 record + 1 lần gọi Expo cho TẤT CẢ token của user (batch),
        // thay vì 1 record + 1 HTTP call cho mỗi token.
        await SendSingleAsync(request, recipient, NotificationChannelEnum.Push, pushChannel,
            isCritical, expoTokens: tokens.Select(t => t.Token).ToList(), ct: ct);
    }

    private async Task SendSingleAsync(
        DispatchRequest request,
        RecipientInfo recipient,
        NotificationChannelEnum channelType,
        INotificationChannel channel,
        bool isCritical,
        IReadOnlyList<string>? expoTokens = null,
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = recipient.UserId,
            Type = request.Type,
            Channel = channelType,
            Status = NotificationStatusEnum.Pending,
            Title = request.Title,
            Body = request.Body,
            PayloadJson = request.PayloadJson,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
        };

        await _unitOfWork.Notifications.AddAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        var sendReq = new SendRequest
        {
            NotificationId = notification.Id,
            UserId = recipient.UserId,
            Type = notification.Type,   // NOTI3-15 (#715) — cần để dựng link hủy theo nhóm
            Title = request.Title,
            Body = request.Body,
            PayloadJson = request.PayloadJson,
            IsCritical = isCritical,
            ExpoToken = expoTokens is { Count: > 0 } ? expoTokens[0] : null,
            ExpoTokens = expoTokens,
            Email = recipient.Email,
            PhoneNumber = recipient.PhoneNumber,
        };

        var result = await channel.SendAsync(sendReq, ct);

        notification.Status = result.Success ? NotificationStatusEnum.Sent : NotificationStatusEnum.Failed;
        notification.SentAt = result.Success ? DateTime.UtcNow : null;
        notification.FailureReason = result.ErrorMessage;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        if (!result.Success)
            _logger.LogWarning("Dispatcher: {Channel} failed for user {UserId}: {Error}",
                channelType, recipient.UserId, result.ErrorMessage);
    }

    // ─────────────────── NOTI-01: gửi 1 record Pending có sẵn (worker path) ───────────────────

    public async Task<DispatchOutcome> DispatchPendingAsync(Notification notification, CancellationToken ct = default)
    {
        if (notification.Status != NotificationStatusEnum.Pending)
            return DispatchOutcome.Sent;   // đã xử lý ở vòng trước — coi như xong, không đụng vào nữa

        var channel = _channels.FirstOrDefault(c => c.ChannelType == notification.Channel);
        if (channel is null)
            return await MarkFailedAsync(notification, $"Không có channel xử lý {notification.Channel}.", ct, "no_channel_handler");

        // Recipient placeholder Guid.Empty là "broadcast chưa resolve" — không gửi được cho ai.
        if (notification.UserId == Guid.Empty)
            return await MarkFailedAsync(notification, "UserId rỗng (placeholder broadcast chưa resolve).", ct, "empty_user_id");

        var pref = await LoadPreferenceAsync(notification.UserId, ct);
        bool isCritical = _criticalTypes.Contains(notification.Type) || HasBypassQuietHoursFlag(notification.PayloadJson);

        // 1) Preference tắt kênh → không bao giờ gửi được, kết thúc luôn (tránh retry vô hạn).
        // NOTI3-04 (#704) — tắt ở cấp kênh HOẶC cấp nhóm × kênh đều dừng ở đây.
        if (!await IsChannelEnabledAsync(pref, notification.Type, notification.Channel, ct))
            return await MarkFailedAsync(notification,
                $"Người dùng đã tắt kênh {notification.Channel} cho nhóm "
                + $"{NotificationCategoryMap.Resolve(notification.Type)} trong tuỳ chọn thông báo.",
                ct, "channel_disabled");

        // 2) Digest (NOTI-12) — gom notification không-critical của user bật digest,
        //    NotificationDigestBackgroundService sẽ gộp và gửi 1 lần.
        //    Bản digest tổng hợp thì KHÔNG gom tiếp (nếu không sẽ tự hoãn chính nó mãi mãi).
        if (!isCritical
            && notification.EntityType != NotificationDigest.EntityType
            && IsDigestChannel(notification.Channel)
            && TryGetDigestWindow(pref, out var window))
            return await DeferAsync(notification, DateTime.UtcNow.Add(window),
                $"Gom vào digest {window.TotalMinutes:0} phút.", ct, "digest");

        // 2b) Hạn mức người dùng (NOTI3-06) — chỉ áp cho kênh CHỦ ĐỘNG (Push/Email/SMS).
        //     InApp không làm phiền ai nên không giới hạn; critical thì bỏ qua hạn mức hoàn toàn —
        //     hạn mức tồn tại để giữ sự chú ý của người dùng, không phải để chặn cảnh báo an toàn.
        //     Vượt trần thì HOÃN (gom vào digest), KHÔNG vứt bỏ: vứt là mất dữ liệu nghiệp vụ.
        if (!isCritical
            && _rateLimiter is not null
            && notification.Channel != NotificationChannelEnum.InApp
            && notification.EntityType != NotificationDigest.EntityType)
        {
            var decision = await _rateLimiter.TryConsumeAsync(notification.UserId, notification.Type, ct);

            if (!decision.Allowed)
            {
                AppMetrics.NotificationRateLimitedTotal
                    .WithLabels(notification.Channel.ToString(), decision.Reason ?? "unknown").Inc();

                var deferUntil = DateTime.UtcNow.AddMinutes(Math.Max(1, _rateLimitOptions.DeferMinutes));
                return await DeferAsync(notification, deferUntil,
                    $"Vượt hạn mức thông báo ({decision.Reason}) — gom vào digest.", ct, "rate_limited");
            }
        }

        // 3) Quiet hours — InApp vẫn ghi được (im lặng), các kênh chủ động thì hoãn tới hết giờ yên.
        if (!isCritical && notification.Channel != NotificationChannelEnum.InApp && IsQuietHours(pref))
            return await DeferAsync(notification, ResolveQuietHoursEndUtc(pref),
                "Đang trong quiet hours — hoãn tới khi hết.", ct, "quiet_hours");

        var account = await LoadAccountAsync(notification.UserId, ct);

        // 4) Thiếu địa chỉ nhận → lỗi vĩnh viễn.
        if (notification.Channel == NotificationChannelEnum.Email && string.IsNullOrWhiteSpace(account?.Email))
            return await MarkFailedAsync(notification, "Người nhận không có email trong read-model.", ct, "no_email");

        if (notification.Channel == NotificationChannelEnum.Sms && string.IsNullOrWhiteSpace(account?.PhoneNumber))
            return await MarkFailedAsync(notification, "Người nhận không có số điện thoại trong read-model.", ct, "no_phone");

        List<string>? tokens = null;
        if (notification.Channel == NotificationChannelEnum.Push)
        {
            tokens = await _unitOfWork.DeviceTokens.GetAllAsync()
                .Where(dt => dt.UserId == notification.UserId && !dt.IsDeleted && dt.IsActive)
                .Select(dt => dt.Token)
                .ToListAsync(ct);

            if (tokens.Count == 0)
                return await MarkFailedAsync(notification, "Người nhận chưa đăng ký device token nào đang hoạt động.", ct, "no_device_token");
        }

        var (title, body) = await RenderContentAsync(notification, ct);

        var sendRequest = new SendRequest
        {
            NotificationId = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type,   // NOTI3-15 (#715) — cần để dựng link hủy theo nhóm
            Title = title,
            Body = body,
            PayloadJson = notification.PayloadJson,
            IsCritical = isCritical,
            ExpoToken = tokens is { Count: > 0 } ? tokens[0] : null,
            ExpoTokens = tokens,
            Email = account?.Email,
            PhoneNumber = account?.PhoneNumber,
        };

        ChannelResult result;
        try
        {
            result = await channel.SendAsync(sendRequest, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dispatch: channel {Channel} ném exception cho notification {Id}",
                notification.Channel, notification.Id);
            result = new ChannelResult(false, ex.Message);
        }

        if (result.Success)
        {
            notification.Status = NotificationStatusEnum.Sent;
            notification.SentAt = DateTime.UtcNow;
            notification.FailureReason = null;
            notification.NextAttemptAt = null;
            notification.DispatchAttemptCount += 1;
            _unitOfWork.Notifications.UpdateAsync(notification);

            // NOTI3-07 (#707) — delivery-rate tách theo channel là metric cơ bản nhất.
            var channelLabel = notification.Channel.ToString();
            AppMetrics.NotificationSentTotal.WithLabels(channelLabel, notification.Type.ToString()).Inc();
            AppMetrics.NotificationDeliveryLatencySeconds
                .WithLabels(channelLabel)
                .Observe(Math.Max(0, (notification.SentAt!.Value - notification.CreatedAt).TotalSeconds));

            await WriteAuditAsync(notification, success: true, reason: null, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return DispatchOutcome.Sent;
        }

        // Lỗi → retry có backoff cho tới MaxAttempts.
        notification.DispatchAttemptCount += 1;
        notification.FailureReason = Truncate(result.ErrorMessage, 1000);

        if (notification.DispatchAttemptCount >= _options.MaxAttempts)
        {
            notification.Status = NotificationStatusEnum.Failed;
            notification.NextAttemptAt = null;
            _unitOfWork.Notifications.UpdateAsync(notification);

            AppMetrics.NotificationFailedTotal
                .WithLabels(notification.Channel.ToString(), "max_attempts_exceeded").Inc();

            await WriteAuditAsync(notification, success: false, notification.FailureReason, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Dispatch: notification {Id} thất bại vĩnh viễn sau {Attempts} lần — {Reason}",
                notification.Id, notification.DispatchAttemptCount, notification.FailureReason);
            return DispatchOutcome.Failed;
        }

        notification.NextAttemptAt = DateTime.UtcNow.Add(BackoffFor(notification.DispatchAttemptCount));
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        AppMetrics.NotificationRetryTotal.WithLabels(notification.Channel.ToString()).Inc();
        return DispatchOutcome.Retrying;
    }

    // ─────────────────────────────────── Helpers ───────────────────────────────────

    /// <summary>
    /// NOTI-14 (#685) — chốt 1 pattern template: nếu DB có template active khớp
    /// (Type × Channel × Locale) thì render nó với PayloadJson; không có thì dùng Title/Body inline
    /// mà consumer đã ghi. Template hỏng KHÔNG chặn gửi — rơi về inline.
    /// </summary>
    private async Task<(string Title, string Body)> RenderContentAsync(Notification notification, CancellationToken ct)
    {
        if (!_options.UseDbTemplates)
            return (notification.Title, notification.Body);

        try
        {
            // Sprint 6.3 NOTI3-12 (#712) — chọn ngôn ngữ theo người nhận, bỏ hardcode "vi-VN".
            var locale = await ResolveLocaleAsync(notification.UserId, ct);

            var template = await _unitOfWork.NotificationTemplates.GetAllAsync()
                .Where(t => !t.IsDeleted
                            && t.IsActive
                            && t.Type == notification.Type
                            && t.Channel == notification.Channel
                            && t.Locale == locale)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync(ct);

            // Sprint 6.3 NOTI3-12 (#712) — locale người nhận không có template thì lùi về mặc định,
            // KHÔNG bỏ template: thà tiếng Việt còn hơn rơi về chuỗi hardcode trong consumer.
            if (template is null && locale != _options.DefaultLocale)
            {
                template = await _unitOfWork.NotificationTemplates.GetAllAsync()
                    .Where(t => !t.IsDeleted
                                && t.IsActive
                                && t.Type == notification.Type
                                && t.Channel == notification.Channel
                                && t.Locale == _options.DefaultLocale)
                    .OrderByDescending(t => t.Version)
                    .FirstOrDefaultAsync(ct);
            }

            if (template is null)
                return (notification.Title, notification.Body);

            var model = BuildTemplateModel(notification);
            var title = string.IsNullOrWhiteSpace(template.TitleTemplate)
                ? notification.Title
                : _templateRenderer.RenderInline(template.TitleTemplate, model);
            var body = string.IsNullOrWhiteSpace(template.BodyTemplate)
                ? notification.Body
                : _templateRenderer.RenderInline(template.BodyTemplate, model);

            return (
                string.IsNullOrWhiteSpace(title) ? notification.Title : title,
                string.IsNullOrWhiteSpace(body) ? notification.Body : body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Dispatch: render DB template lỗi cho notification {Id} — dùng nội dung inline.",
                notification.Id);
            return (notification.Title, notification.Body);
        }
    }

    private static Dictionary<string, object?> BuildTemplateModel(Notification notification)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = notification.Title,
            ["Body"] = notification.Body,
            ["EntityType"] = notification.EntityType,
            ["EntityId"] = notification.EntityId,
            ["UserId"] = notification.UserId,
            ["CreatedAt"] = notification.CreatedAt,
        };

        if (string.IsNullOrWhiteSpace(notification.PayloadJson))
            return model;

        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(notification.PayloadJson);
            if (payload is null)
                return model;

            foreach (var (key, value) in payload)
                model[key] = JsonElementToObject(value);
        }
        catch (JsonException)
        {
            // Payload không phải JSON object — bỏ qua, model vẫn có các field cơ bản.
        }

        return model;
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.ToString(),
    };

    private async Task<AccountReadModel?> LoadAccountAsync(Guid userId, CancellationToken ct) =>
        await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == userId && !a.IsDeleted, ct);

    /// <param name="metricReason">
    /// Nhóm lý do NGẮN dùng làm label Prometheus (vd "channel_disabled"). Tuyệt đối không truyền
    /// message thô vào đây — mỗi giá trị khác nhau tạo 1 time-series mới, sẽ nổ cardinality.
    /// </param>
    private async Task<DispatchOutcome> MarkFailedAsync(
        Notification notification, string reason, CancellationToken ct, string metricReason = "unknown")
    {
        AppMetrics.NotificationFailedTotal
            .WithLabels(notification.Channel.ToString(), metricReason).Inc();

        notification.Status = NotificationStatusEnum.Failed;
        notification.FailureReason = Truncate(reason, 1000);
        notification.NextAttemptAt = null;
        notification.DispatchAttemptCount += 1;
        _unitOfWork.Notifications.UpdateAsync(notification);

        await WriteAuditAsync(notification, success: false, reason, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Dispatch: notification {Id} kết thúc Failed — {Reason}", notification.Id, reason);
        return DispatchOutcome.Failed;
    }

    private async Task<DispatchOutcome> DeferAsync(
        Notification notification, DateTime nextAttemptUtc, string reason, CancellationToken ct, string metricReason = "unknown")
    {
        // Hoãn KHÔNG tính là 1 lần thử — nếu không, quiet hours dài sẽ ăn hết MaxAttempts.
        notification.NextAttemptAt = nextAttemptUtc;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        AppMetrics.NotificationDeferredTotal
            .WithLabels(notification.Channel.ToString(), metricReason).Inc();

        _logger.LogDebug("Dispatch: hoãn notification {Id} tới {Next} — {Reason}",
            notification.Id, nextAttemptUtc, reason);
        return DispatchOutcome.Deferred;
    }

    /// <summary>NOTI-13 (#684) — chỉ Push/InApp nằm trong 7 action của #AUDIT-34; Email/Sms bỏ qua.</summary>
    private async Task WriteAuditAsync(Notification notification, bool success, string? reason, CancellationToken ct)
    {
        NotificationAuditActionEnum? action = notification.Channel switch
        {
            NotificationChannelEnum.Push => success
                ? NotificationAuditActionEnum.PushSent
                : NotificationAuditActionEnum.PushFailed,
            NotificationChannelEnum.InApp when success => NotificationAuditActionEnum.InAppCreated,
            _ => null,
        };

        if (action is null)
            return;

        await _auditWriter.WriteAsync(
            action.Value,
            notification.Id,
            notification.UserId,
            success,
            reason,
            new Dictionary<string, object?>
            {
                ["channel"] = notification.Channel.ToString(),
                ["type"] = notification.Type.ToString(),
                ["attempt"] = notification.DispatchAttemptCount,
                ["entityType"] = notification.EntityType,
                ["entityId"] = notification.EntityId,
            },
            ct);
    }

    private TimeSpan BackoffFor(int attempt)
    {
        // attempt = 1 → Base; 2 → Base*2; 3 → Base*4 … chặn trần MaxBackoffSeconds.
        var seconds = _options.BaseBackoffSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxBackoffSeconds));
    }

    /// <summary>Digest chỉ áp cho kênh chủ động ồn ào (Email/Push); InApp/SMS gửi ngay.</summary>
    private static bool IsDigestChannel(NotificationChannelEnum channel) =>
        channel is NotificationChannelEnum.Email or NotificationChannelEnum.Push;

    /// <summary>
    /// NOTI-12 (#683) — quy đổi preference thành cửa sổ digest.
    /// <c>DigestWindowMinutes</c> ưu tiên; nếu null mà <c>Frequency = Daily</c> thì coi như 24 giờ.
    /// </summary>
    internal static bool TryGetDigestWindow(NotificationPreference pref, out TimeSpan window)
    {
        if (pref.DigestWindowMinutes is > 0)
        {
            window = TimeSpan.FromMinutes(pref.DigestWindowMinutes.Value);
            return true;
        }

        if (pref.Frequency == NotificationFrequencyEnum.Daily)
        {
            window = TimeSpan.FromDays(1);
            return true;
        }

        window = TimeSpan.Zero;
        return false;
    }

    /// <summary>Đọc cờ <c>bypassQuietHours</c> mà CreateNotificationCommandHandler merge vào payload (#IoT2-31).</summary>
    private static bool HasBypassQuietHoursFlag(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("bypassQuietHours", out var flag)
                   && flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-12 (#712) — locale của người nhận, lấy từ read-model account.
    /// Không có thì dùng <c>Notification:Dispatch:DefaultLocale</c>.
    /// </summary>
    private async Task<string> ResolveLocaleAsync(Guid userId, CancellationToken ct)
    {
        var account = await LoadAccountAsync(userId, ct);
        var locale = account?.PreferredLocale;

        return string.IsNullOrWhiteSpace(locale) ? _options.DefaultLocale : locale;
    }

    private async Task<NotificationPreference> LoadPreferenceAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = $"notif_pref:{userId}";
        var cached = await _cache.GetAsync<NotificationPreference>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var pref = await _unitOfWork.NotificationPreferences.GetAllAsync()
            .FirstOrDefaultAsync(p => p.UserId == userId && !p.IsDeleted, ct);

        pref ??= new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
        };

        await _cache.SetAsync(cacheKey, pref, TimeSpan.FromMinutes(5), ct);
        return pref;
    }

    private static TimeZoneInfo ResolveTimeZone(NotificationPreference pref)
    {
        try
        { return TimeZoneInfo.FindSystemTimeZoneById(pref.TimeZone); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
    }

    private static bool IsQuietHours(NotificationPreference pref)
    {
        if (pref.QuietHoursStart is null || pref.QuietHoursEnd is null)
            return false;

        var tz = ResolveTimeZone(pref);
        var now = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).TimeOfDay);
        var start = pref.QuietHoursStart.Value;
        var end = pref.QuietHoursEnd.Value;

        // Wraps midnight (e.g. 22:00–07:00)
        return start > end ? now >= start || now < end : now >= start && now < end;
    }

    /// <summary>
    /// Thời điểm UTC quiet hours kết thúc — mốc hẹn lại cho record bị hoãn.
    /// Cộng thêm 1 phút đệm để tránh tick đúng biên rồi lại bị coi là còn trong quiet hours.
    /// </summary>
    private static DateTime ResolveQuietHoursEndUtc(NotificationPreference pref)
    {
        if (pref.QuietHoursEnd is null)
            return DateTime.UtcNow.AddMinutes(15);

        var tz = ResolveTimeZone(pref);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var end = pref.QuietHoursEnd.Value;

        var localEnd = new DateTime(localNow.Year, localNow.Month, localNow.Day, end.Hour, end.Minute, 0, DateTimeKind.Unspecified);
        if (localEnd <= localNow)
            localEnd = localEnd.AddDays(1);

        return TimeZoneInfo.ConvertTimeToUtc(localEnd, tz).AddMinutes(1);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-04 (#704) — kiểm tra tuỳ chọn ở CẢ HAI cấp: kênh toàn cục và nhóm × kênh.
    ///
    /// Quan hệ là **và logic**: công tắc lớn (tắt Email) thắng mọi tuỳ chọn nhóm — đó là điều người
    /// dùng mong đợi khi gạt nó. Ngược lại, chưa có dòng tuỳ chọn nhóm nào thì coi như chưa tuỳ chỉnh
    /// và rơi về hành vi cũ, nên dữ liệu cũ không cần backfill.
    /// </summary>
    private async Task<bool> IsChannelEnabledAsync(
        NotificationPreference pref, NotificationTypeEnum type, NotificationChannelEnum channel, CancellationToken ct)
    {
        if (!IsChannelEnabled(pref, channel))
            return false;

        var category = NotificationCategoryMap.Resolve(type);
        var categoryPref = await LoadCategoryPreferenceAsync(pref.UserId, category, ct);

        return categoryPref is null || IsCategoryChannelEnabled(categoryPref, channel);
    }

    private static bool IsCategoryChannelEnabled(NotificationCategoryPreference pref, NotificationChannelEnum channel) =>
        channel switch
        {
            NotificationChannelEnum.Push => pref.PushEnabled,
            NotificationChannelEnum.Email => pref.EmailEnabled,
            NotificationChannelEnum.Sms => pref.SmsEnabled,
            NotificationChannelEnum.InApp => pref.InAppEnabled,
            _ => false,
        };

    /// <summary>
    /// Sprint 6.3 NOTI3-04 (#704) — đọc tuỳ chọn nhóm, có cache 5' như preference kênh.
    /// Trả <c>null</c> nghĩa là người dùng CHƯA tuỳ chỉnh nhóm này (khác với "đã tắt hết").
    /// </summary>
    private async Task<NotificationCategoryPreference?> LoadCategoryPreferenceAsync(
        Guid userId, NotificationCategoryEnum category, CancellationToken ct)
    {
        var cacheKey = $"notif_cat_pref:{userId}:{(int)category}";

        // Sentinel: cache cả trường hợp "chưa tuỳ chỉnh" để không truy vấn DB lại mỗi lần gửi.
        var cached = await _cache.GetAsync<CategoryPreferenceCacheEntry>(cacheKey, ct);
        if (cached is not null)
            return cached.Preference;

        var pref = await _unitOfWork.NotificationCategoryPreferences.GetAllAsync()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category && !p.IsDeleted, ct);

        await _cache.SetAsync(cacheKey, new CategoryPreferenceCacheEntry { Preference = pref },
            TimeSpan.FromMinutes(5), ct);

        return pref;
    }

    /// <summary>Bọc để phân biệt "cache miss" với "đã cache và kết quả là chưa tuỳ chỉnh".</summary>
    private sealed class CategoryPreferenceCacheEntry
    {
        public NotificationCategoryPreference? Preference { get; set; }
    }

    private static bool IsChannelEnabled(NotificationPreference pref, NotificationChannelEnum channel) =>
        channel switch
        {
            NotificationChannelEnum.Push => pref.PushEnabled,
            NotificationChannelEnum.Email => pref.EmailEnabled,
            NotificationChannelEnum.Sms => pref.SmsEnabled,
            NotificationChannelEnum.InApp => pref.InAppEnabled,
            _ => false,
        };

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
