using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Middleware;

namespace NotificationService.Infrastructure.Services;

/// <summary>
/// Sprint 6.2 NOTI-13 (#684) — implement <see cref="INotificationAuditWriter"/>.
/// Pattern bám <c>AuthService.AuditTrailNotificationHandler</c> (#AUDIT-09): ghi log + outbox trong
/// cùng UnitOfWork, không SaveChanges, nuốt lỗi (audit không được phá vỡ luồng gửi notification).
/// </summary>
public class NotificationAuditWriter : INotificationAuditWriter
{
    private const string ServiceName = "NotificationService";
    private const int MaxReasonLength = 500;
    private const int MaxUserAgentLength = 500;

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<NotificationAuditWriter> _logger;

    public NotificationAuditWriter(
        INotificationUnitOfWork unitOfWork,
        ILogger<NotificationAuditWriter> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task WriteAsync(
        NotificationAuditActionEnum action,
        Guid notificationId,
        Guid userId,
        bool isSuccess,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            var (actionCode, category, severity) = Classify(action, isSuccess);
            var eventId = AuditEventId.New();
            var now = DateTime.UtcNow;

            string? ip = null;
            string? userAgent = null;
            Guid? correlationGuid = null;

            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext is not null)
            {
                ip = httpContext.Connection?.RemoteIpAddress?.ToString();
                userAgent = Truncate(httpContext.Request?.Headers.UserAgent.ToString(), MaxUserAgentLength);
                if (Guid.TryParse(httpContext.GetCorrelationId(), out var parsed) && parsed != Guid.Empty)
                    correlationGuid = parsed;
            }

            var metadataJson = metadata is { Count: > 0 } ? JsonSerializer.Serialize(metadata) : null;
            var truncatedReason = Truncate(reason, MaxReasonLength);

            await _unitOfWork.NotificationAuditLogs.AddAsync(new NotificationAuditLog
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ServiceName = ServiceName,
                ActionCode = actionCode,
                ActionCategory = category,
                Severity = severity,
                TargetType = TargetTypes.Notification,
                TargetId = notificationId,
                TargetDisplay = notificationId.ToString(),
                // Notification do hệ thống phát: actor chính là user nhận (không có người thao tác).
                ActorAccountId = userId == Guid.Empty ? null : userId,
                ActorRole = null,
                ActorDisplay = null,
                ActorIp = ip,
                ActorUserAgent = userAgent,
                IsSuccess = isSuccess,
                ErrorCode = isSuccess ? null : actionCode,
                Reason = truncatedReason,
                MetadataJson = metadataJson,
                CorrelationId = correlationGuid,
                CausationId = null,
                OccurredAt = now,
                RecordedAt = now,
            });

            var integrationEvent = new AuditCreatedEventV1(
                EventId: eventId,
                ServiceName: ServiceName,
                ActionCode: actionCode,
                ActionCategory: category,
                Severity: severity,
                TargetType: TargetTypes.Notification,
                TargetId: notificationId,
                TargetDisplay: notificationId.ToString(),
                ActorAccountId: userId == Guid.Empty ? null : userId,
                ActorRole: null,
                ActorDisplay: null,
                ActorIp: ip,
                ActorUserAgent: userAgent,
                IsSuccess: isSuccess,
                ErrorCode: isSuccess ? null : actionCode,
                Reason: truncatedReason,
                MetadataJson: metadataJson,
                CorrelationId: correlationGuid,
                CausationId: null,
                OccurredAt: now,
                RecordedAt: now);

            await _unitOfWork.NotificationAuditOutboxes.AddAsync(new NotificationAuditOutbox
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                EventType = nameof(AuditCreatedEventV1),
                Payload = JsonSerializer.Serialize(integrationEvent),
                Status = AuditOutboxStatusEnum.Pending,
            });
        }
        catch (Exception ex)
        {
            // Audit fail KHÔNG được làm hỏng việc gửi notification.
            _logger.LogError(ex,
                "Ghi notification audit thất bại. Action={Action} NotificationId={NotificationId}",
                action, notificationId);
        }
    }

    /// <summary>Map action → (ActionCode, Category, Severity) theo #AUDIT-02/#AUDIT-34.</summary>
    private static (string ActionCode, string Category, string Severity) Classify(
        NotificationAuditActionEnum action, bool isSuccess) =>
        action switch
        {
            NotificationAuditActionEnum.PushSent =>
                (ActionCodes.Notification.PushSent, AuditCategories.Communication, isSuccess ? Severities.Info : Severities.Warning),
            NotificationAuditActionEnum.PushFailed =>
                (ActionCodes.Notification.PushFailed, AuditCategories.Communication, Severities.Warning),
            NotificationAuditActionEnum.PushDelivered =>
                (ActionCodes.Notification.PushDelivered, AuditCategories.Communication, Severities.Info),
            NotificationAuditActionEnum.PushOpened =>
                (ActionCodes.Notification.PushOpened, AuditCategories.Communication, Severities.Info),
            NotificationAuditActionEnum.InAppCreated =>
                (ActionCodes.Notification.InAppCreated, AuditCategories.Communication, Severities.Info),
            NotificationAuditActionEnum.InAppRead =>
                (ActionCodes.Notification.InAppRead, AuditCategories.Communication, Severities.Info),
            NotificationAuditActionEnum.InAppDismissed =>
                (ActionCodes.Notification.InAppDismissed, AuditCategories.Communication, Severities.Info),
            // Sprint 6.3 NOTI3-12 (#712) — Warning chứ không Info: gửi email thật từ domain hệ thống
            // là hành động cần nổi lên trong bộ lọc audit, không nên lẫn vào nhiễu Info.
            NotificationAuditActionEnum.TemplateTestSent =>
                (ActionCodes.Notification.TemplateTestSent, AuditCategories.Communication, Severities.Warning),
            _ => (action.ToString(), AuditCategories.Communication, Severities.Info),
        };

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
