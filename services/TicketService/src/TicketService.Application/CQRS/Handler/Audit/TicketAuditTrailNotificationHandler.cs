using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Middleware;
using SharedInfrastructure.RateLimiting;
using SharedInfrastructure.Services;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Audit;

/// <summary>
/// Ghi TicketAuditLog + TicketAuditOutbox (Sprint audit #AUDIT-25). KHÔNG SaveChanges — command handler save atomic.
/// Audit fail KHÔNG throw.
/// </summary>
public class TicketAuditTrailNotificationHandler : INotificationHandler<TicketAuditTrailNotification>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<TicketAuditTrailNotificationHandler> _logger;

    public TicketAuditTrailNotificationHandler(
        ITicketUnitOfWork unitOfWork,
        ICurrentUserService currentUserService,
        ILogger<TicketAuditTrailNotificationHandler> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task Handle(TicketAuditTrailNotification n, CancellationToken cancellationToken)
    {
        try
        {
            var http = _httpContextAccessor?.HttpContext;
            // #27 QA solars.io.vn 2026-08-29: RemoteIpAddress sau ApiGateway luôn là IP pod của
            // gateway (vd "10.42.0.8"), không phải IP trình duyệt thật. Ưu tiên header X-Client-Ip
            // mà gateway ghi đè (anti-spoof, xem RateLimitPartitionResolver) — cùng cơ chế IP dùng
            // cho rate limiting.
            var gatewayIp = http?.Request.Headers[RateLimitPartitionResolver.ClientIpHeader].FirstOrDefault();
            var ip = !string.IsNullOrWhiteSpace(gatewayIp) ? gatewayIp.Trim() : http?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = http?.Request?.Headers.UserAgent.ToString();
            Guid.TryParse(http?.GetCorrelationId(), out var correlationGuid);

            Guid? actor = null;
            if (Guid.TryParse(_currentUserService.UserId, out var resolvedActor))
                actor = resolvedActor;

            string? metadataJson = n.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(n.Metadata) : null;
            var eventId = AuditEventId.New();    // #AUDIT-04 — helper tập trung event_id.
            var now = DateTime.UtcNow;

            await _unitOfWork.TicketAuditLogs.AddAsync(new TicketAuditLog
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ServiceName = "TicketService",
                ActionCode = n.ActionCode,
                ActionCategory = n.ActionCategory,
                Severity = n.Severity,
                TargetType = TargetTypes.Ticket,
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

            var evt = new AuditCreatedEventV1(eventId, "TicketService", n.ActionCode, n.ActionCategory, n.Severity,
                TargetTypes.Ticket, n.TargetId, Truncate(n.TargetDisplay, 255),
                actor, null, null, ip, Truncate(userAgent, 512),
                n.IsSuccess, n.IsSuccess ? null : n.ActionCode, Truncate(n.Reason, 1024), metadataJson,
                correlationGuid == Guid.Empty ? null : correlationGuid, n.CausationId, now, now);

            await _unitOfWork.TicketAuditOutboxes.AddAsync(new TicketAuditOutbox
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
            _logger.LogError(ex, "Failed to write ticket audit. Action={Action}", n.ActionCode);
        }
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrEmpty(v) ? v : v.Length <= max ? v : v[..max];
}
