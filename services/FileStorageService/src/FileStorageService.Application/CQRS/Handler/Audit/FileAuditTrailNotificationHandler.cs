using System.Security.Claims;
using System.Text.Json;
using FileStorageService.Application.CQRS.Notification.Audit;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Middleware;
using SharedInfrastructure.RateLimiting;

namespace FileStorageService.Application.CQRS.Handler.Audit;

/// <summary>
/// Ghi FileAuditLog + FileAuditOutbox (Sprint audit #AUDIT-29). KHÔNG SaveChanges — caller save atomic.
/// Audit fail KHÔNG throw (không phá luồng upload/download/delete file).
/// </summary>
public class FileAuditTrailNotificationHandler : INotificationHandler<FileAuditTrailNotification>
{
    private readonly IFileStorageUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<FileAuditTrailNotificationHandler> _logger;

    public FileAuditTrailNotificationHandler(
        IFileStorageUnitOfWork unitOfWork,
        ILogger<FileAuditTrailNotificationHandler> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task Handle(FileAuditTrailNotification n, CancellationToken cancellationToken)
    {
        try
        {
            var http = _httpContextAccessor?.HttpContext;
            // #27 QA solars.io.vn 2026-08-29: RemoteIpAddress sau ApiGateway luôn là IP pod của
            // gateway, không phải IP trình duyệt thật. Ưu tiên header X-Client-Ip mà gateway ghi đè
            // (anti-spoof, xem RateLimitPartitionResolver).
            var gatewayIp = http?.Request.Headers[RateLimitPartitionResolver.ClientIpHeader].FirstOrDefault();
            var ip = !string.IsNullOrWhiteSpace(gatewayIp) ? gatewayIp.Trim() : http?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = http?.Request?.Headers.UserAgent.ToString();
            Guid.TryParse(http?.GetCorrelationId(), out var correlationGuid);

            var user = http?.User;
            var rawUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("AccountId");
            Guid? actor = Guid.TryParse(rawUserId, out var resolvedActor) ? resolvedActor : null;
            var actorRole = user?.FindFirstValue(ClaimTypes.Role) ?? user?.FindFirstValue("role");

            string? metadataJson = n.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(n.Metadata) : null;
            var eventId = AuditEventId.New();    // #AUDIT-04 — helper tập trung event_id.
            var now = DateTime.UtcNow;

            await _unitOfWork.FileAuditLogs.AddAsync(new FileAuditLog
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ServiceName = "FileStorageService",
                ActionCode = n.ActionCode,
                ActionCategory = n.ActionCategory,
                Severity = n.Severity,
                TargetType = TargetTypes.File,
                TargetId = n.TargetId,
                TargetDisplay = Truncate(n.TargetDisplay, 255),
                ActorAccountId = actor,
                ActorRole = actorRole,
                ActorIp = ip,
                ActorUserAgent = Truncate(userAgent, 512),
                IsSuccess = n.IsSuccess,
                ErrorCode = n.IsSuccess ? null : n.ActionCode,
                Reason = Truncate(n.Reason, 1024),
                MetadataJson = metadataJson,
                CorrelationId = correlationGuid == Guid.Empty ? null : correlationGuid,
                OccurredAt = now,
                RecordedAt = now,
            });

            var evt = new AuditCreatedEventV1(eventId, "FileStorageService", n.ActionCode, n.ActionCategory, n.Severity,
                TargetTypes.File, n.TargetId, Truncate(n.TargetDisplay, 255),
                actor, actorRole, null, ip, Truncate(userAgent, 512),
                n.IsSuccess, n.IsSuccess ? null : n.ActionCode, Truncate(n.Reason, 1024), metadataJson,
                correlationGuid == Guid.Empty ? null : correlationGuid, null, now, now);

            await _unitOfWork.FileAuditOutboxes.AddAsync(new FileAuditOutbox
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
            _logger.LogError(ex, "Failed to write file audit. Action={Action}", n.ActionCode);
        }
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrEmpty(v) ? v : v.Length <= max ? v : v[..max];
}
