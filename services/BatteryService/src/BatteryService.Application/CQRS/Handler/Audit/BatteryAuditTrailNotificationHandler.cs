using System.Text.Json;
using BatteryService.Application.CQRS.Notification.Audit;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Middleware;
using SharedInfrastructure.Services;

namespace BatteryService.Application.CQRS.Handler.Audit;

/// <summary>
/// Ghi BatteryAuditLog + BatteryAuditOutbox (Sprint audit #AUDIT-21). KHÔNG SaveChanges — command handler save atomic.
/// Audit fail KHÔNG throw (không phá business flow).
/// </summary>
public class BatteryAuditTrailNotificationHandler : INotificationHandler<BatteryAuditTrailNotification>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<BatteryAuditTrailNotificationHandler> _logger;

    public BatteryAuditTrailNotificationHandler(
        IBatteryUnitOfWork unitOfWork,
        ICurrentUserService currentUserService,
        ILogger<BatteryAuditTrailNotificationHandler> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task Handle(BatteryAuditTrailNotification n, CancellationToken cancellationToken)
    {
        try
        {
            var http = _httpContextAccessor?.HttpContext;
            var ip = http?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = http?.Request?.Headers.UserAgent.ToString();
            var correlationStr = http?.GetCorrelationId();
            Guid.TryParse(correlationStr, out var correlationGuid);

            Guid? actor = null;
            if (Guid.TryParse(_currentUserService.UserId, out var resolvedActor))
                actor = resolvedActor;

            string? metadataJson = n.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(n.Metadata) : null;
            var eventId = AuditEventId.New();    // #AUDIT-04 — helper tập trung event_id.
            var now = DateTime.UtcNow;

            await _unitOfWork.BatteryAuditLogs.AddAsync(new BatteryAuditLog
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ServiceName = "BatteryService",
                ActionCode = n.ActionCode,
                ActionCategory = n.ActionCategory,
                Severity = n.Severity,
                TargetType = TargetTypes.Battery,
                TargetId = n.TargetId,
                TargetDisplay = Truncate(n.TargetDisplay, 255),
                ActorAccountId = actor,
                ActorIp = ip,
                ActorUserAgent = Truncate(userAgent, 512),
                IsSuccess = n.IsSuccess,
                ErrorCode = n.IsSuccess ? null : n.ActionCode,
                Reason = Truncate(n.Reason, 1024),
                MetadataJson = metadataJson,
                CorrelationId = correlationGuid == Guid.Empty ? null : correlationGuid,
                CausationId = n.CausationId,
                OccurredAt = now,
                RecordedAt = now,
            });

            var evt = new AuditCreatedEventV1(eventId, "BatteryService", n.ActionCode, n.ActionCategory, n.Severity,
                TargetTypes.Battery, n.TargetId, Truncate(n.TargetDisplay, 255),
                actor, null, null, ip, Truncate(userAgent, 512),
                n.IsSuccess, n.IsSuccess ? null : n.ActionCode, Truncate(n.Reason, 1024), metadataJson,
                correlationGuid == Guid.Empty ? null : correlationGuid, n.CausationId, now, now);

            await _unitOfWork.BatteryAuditOutboxes.AddAsync(new BatteryAuditOutbox
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                EventType = nameof(AuditCreatedEventV1),
                Payload = JsonSerializer.Serialize(evt),
                Status = AuditOutboxStatusEnum.Pending,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write battery audit. Action={Action}", n.ActionCode);
        }
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrEmpty(v) ? v : v.Length <= max ? v : v[..max];
}
