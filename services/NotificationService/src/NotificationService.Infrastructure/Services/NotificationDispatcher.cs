using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Interfaces;

namespace NotificationService.Infrastructure.Services;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    private static readonly NotificationChannelEnum[] _defaultChannels = [NotificationChannelEnum.InApp];

    /// <summary>Khớp NotificationConfiguration: <c>FailureReason</c> HasMaxLength(1000).</summary>
    private const int FailureReasonMaxLength = 1000;

    // overall.md §3.4 — Type → Channel matrix
    private static readonly Dictionary<NotificationTypeEnum, NotificationChannelEnum[]> TypeChannelMatrix = new()
    {
        [NotificationTypeEnum.TicketCreated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.TicketAssigned] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
        [NotificationTypeEnum.TicketStatusChanged] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.TicketResolved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
        [NotificationTypeEnum.TicketClosed] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.TicketEscalated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.SlaWarning] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.SlaBreached] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
        [NotificationTypeEnum.BatteryAnomalyDetected] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email],
        [NotificationTypeEnum.EnvironmentalIncidentDetected] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
        [NotificationTypeEnum.EnvironmentalIncidentResolved] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.IncidentDeclared] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email, NotificationChannelEnum.Sms],
        [NotificationTypeEnum.AccountActivated] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Email],
        [NotificationTypeEnum.AdminInvite] = [NotificationChannelEnum.Email],
        [NotificationTypeEnum.BatteryAlertEscalationPending] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.AlertTicketSagaFailed] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.IotDeviceWentOffline] = [NotificationChannelEnum.InApp, NotificationChannelEnum.Push],
        [NotificationTypeEnum.System] = [NotificationChannelEnum.InApp],
    };

    // overall.md §3.4 — Critical types bypass quiet hours automatically
    private static readonly HashSet<NotificationTypeEnum> CriticalTypes =
    [
        NotificationTypeEnum.EnvironmentalIncidentDetected,
        NotificationTypeEnum.IncidentDeclared,
        NotificationTypeEnum.BatteryAlertEscalationPending,
        NotificationTypeEnum.AlertTicketSagaFailed,
        NotificationTypeEnum.SlaBreached,
    ];

    public NotificationDispatcher(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        IEnumerable<INotificationChannel> channels,
        ILogger<NotificationDispatcher> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _channels = channels;
        _logger = logger;
    }

    public async Task DispatchAsync(DispatchRequest request, CancellationToken ct = default)
    {
        if (request.Recipients.Count == 0)
            return;

        var targetChannels = TypeChannelMatrix.GetValueOrDefault(request.Type, _defaultChannels);
        bool isCritical = request.BypassQuietHours || CriticalTypes.Contains(request.Type);

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
                if (!IsChannelEnabled(pref, channelType))
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

    public async Task<bool> DispatchPendingAsync(Notification notification, CancellationToken ct = default)
    {
        if (notification.IsDeleted || notification.Status != NotificationStatusEnum.Pending)
            return false;

        // #673 (NOTI-02) chưa merge → SendNotificationEmailEvent chưa có consumer, message bị bus
        // drop im lặng. Giữ Pending để gửi lại khi #673 land — GỠ nhánh này lúc đó.
        if (notification.Channel == NotificationChannelEnum.Email)
            return false;

        var pref = await LoadPreferenceAsync(notification.UserId, ct);

        if (!IsChannelEnabled(pref, notification.Channel))
            return await MarkFailedAsync(notification, $"Channel {notification.Channel} disabled by preference", ct);

        bool isCritical = CriticalTypes.Contains(notification.Type) || HasBypassQuietHours(notification.PayloadJson);
        if (!isCritical && notification.Channel != NotificationChannelEnum.InApp && IsQuietHours(pref))
            return false;

        var channel = _channels.FirstOrDefault(c => c.ChannelType == notification.Channel);
        if (channel is null)
            return await MarkFailedAsync(notification, $"No channel registered for {notification.Channel}", ct);

        if (notification.Channel == NotificationChannelEnum.Push)
            return await SendPushAsync(notification, channel, isCritical, ct);

        // AccountReadModel có thể chưa sync (null) — chỉ Sms bắt buộc phải có số điện thoại.
        var account = await _unitOfWork.Accounts.GetAllAsync(false)
            .FirstOrDefaultAsync(a => a.Id == notification.UserId && !a.IsDeleted, ct);

        if (notification.Channel == NotificationChannelEnum.Sms && string.IsNullOrWhiteSpace(account?.PhoneNumber))
            return await MarkFailedAsync(notification, "No phone number for recipient", ct);

        var sendReq = BuildSendRequest(notification, isCritical, email: account?.Email, phoneNumber: account?.PhoneNumber);
        var result = await channel.SendAsync(sendReq, ct);

        return result.Success
            ? await MarkSentAsync(notification, ct)
            : await MarkFailedAsync(notification, result.ErrorMessage ?? "Send failed", ct);
    }

    /// <summary>Fan-out 1 record Push ra toàn bộ device token active — ≥1 token thành công là Sent.</summary>
    private async Task<bool> SendPushAsync(
        Notification notification,
        INotificationChannel pushChannel,
        bool isCritical,
        CancellationToken ct)
    {
        var tokens = await _unitOfWork.DeviceTokens.GetAllAsync(false)
            .Where(dt => dt.UserId == notification.UserId && !dt.IsDeleted && dt.IsActive)
            .ToListAsync(ct);

        if (tokens.Count == 0)
            return await MarkFailedAsync(notification, "No active device token", ct);

        bool anySuccess = false;
        string? lastError = null;

        foreach (var token in tokens)
        {
            var result = await pushChannel.SendAsync(
                BuildSendRequest(notification, isCritical, expoToken: token.Token), ct);

            if (result.Success)
                anySuccess = true;
            else
                lastError = result.ErrorMessage;
        }

        return anySuccess
            ? await MarkSentAsync(notification, ct)
            : await MarkFailedAsync(notification, lastError ?? "Push failed", ct);
    }

    private async Task<bool> MarkSentAsync(Notification notification, CancellationToken ct)
    {
        notification.Status = NotificationStatusEnum.Sent;
        notification.SentAt = DateTime.UtcNow;
        notification.FailureReason = null;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> MarkFailedAsync(Notification notification, string reason, CancellationToken ct)
    {
        notification.Status = NotificationStatusEnum.Failed;
        notification.SentAt = null;
        // reason có thể là ex.Message của channel (không giới hạn độ dài) — cắt theo giới hạn column
        // để SaveChangesAsync không throw, nếu không row sẽ kẹt Pending và bị worker retry mãi.
        notification.FailureReason = reason.Length > FailureReasonMaxLength
            ? reason[..FailureReasonMaxLength]
            : reason;
        _unitOfWork.Notifications.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning("Dispatcher: notification {NotificationId} ({Channel}) failed: {Reason}",
            notification.Id, notification.Channel, reason);
        return true;
    }

    /// <summary>
    /// Đọc cờ <c>bypassQuietHours</c> mà CreateNotificationCommandHandler nhét vào PayloadJson.
    /// Payload không phải JSON hợp lệ → false, không throw.
    /// </summary>
    private static bool HasBypassQuietHours(string? payloadJson)
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

    private static SendRequest BuildSendRequest(
        Notification notification,
        bool isCritical,
        string? expoToken = null,
        string? email = null,
        string? phoneNumber = null) =>
        new()
        {
            NotificationId = notification.Id,
            UserId = notification.UserId,
            Title = notification.Title,
            Body = notification.Body,
            PayloadJson = notification.PayloadJson,
            IsCritical = isCritical,
            ExpoToken = expoToken,
            Email = email,
            PhoneNumber = phoneNumber,
        };

    private async Task DispatchPushAsync(
        DispatchRequest request,
        RecipientInfo recipient,
        NotificationPreference pref,
        bool isQuiet,
        CancellationToken ct)
    {
        if (!pref.PushEnabled || isQuiet)
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

        bool isCritical = CriticalTypes.Contains(request.Type) || request.BypassQuietHours;
        foreach (var token in tokens)
            await SendSingleAsync(request, recipient, NotificationChannelEnum.Push, pushChannel,
                isCritical, expoToken: token.Token, ct: ct);
    }

    private async Task SendSingleAsync(
        DispatchRequest request,
        RecipientInfo recipient,
        NotificationChannelEnum channelType,
        INotificationChannel channel,
        bool isCritical,
        string? expoToken = null,
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
            Title = request.Title,
            Body = request.Body,
            PayloadJson = request.PayloadJson,
            IsCritical = isCritical,
            ExpoToken = expoToken,
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

    private static bool IsQuietHours(NotificationPreference pref)
    {
        if (pref.QuietHoursStart is null || pref.QuietHoursEnd is null)
            return false;

        TimeZoneInfo tz;
        try
        { tz = TimeZoneInfo.FindSystemTimeZoneById(pref.TimeZone); }
        catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }

        var now = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).TimeOfDay);
        var start = pref.QuietHoursStart.Value;
        var end = pref.QuietHoursEnd.Value;

        // Wraps midnight (e.g. 22:00–07:00)
        return start > end ? now >= start || now < end : now >= start && now < end;
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
}
