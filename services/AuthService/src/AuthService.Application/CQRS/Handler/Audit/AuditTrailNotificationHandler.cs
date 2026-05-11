using System.Text.Json;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Middleware;
using SharedInfrastructure.Services;

namespace AuthService.Application.CQRS.Handler.Audit;

/// <summary>
/// Subscribe <see cref="AuditTrailNotification"/> và Add entry vào DbContext.AuditLogs.
/// KHÔNG gọi SaveChangesAsync — để Command Handler đang publish notification SaveChanges atomic với business data.
///
/// Nếu ghi audit log thất bại (vd serialize metadata lỗi), log error nhưng KHÔNG throw —
/// failure của audit không được phá vỡ business flow.
/// </summary>
public class AuditTrailNotificationHandler : INotificationHandler<AuditTrailNotification>
{
    private const int MaxReasonLength = 500;
    private const int MaxUserAgentLength = 500;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<AuditTrailNotificationHandler> _logger;

    public AuditTrailNotificationHandler(
        IAuthUnitOfWork unitOfWork,
        ICurrentUserService currentUserService,
        ILogger<AuditTrailNotificationHandler> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task Handle(AuditTrailNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var (ip, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);
            var correlationId = _httpContextAccessor?.HttpContext?.GetCorrelationId();

            Guid? actor = notification.ActorAccountIdOverride;
            if (actor == null && Guid.TryParse(_currentUserService.UserId, out var resolvedActor))
                actor = resolvedActor;

            string? metadataJson = null;
            if (notification.Metadata != null && notification.Metadata.Count > 0)
                metadataJson = JsonSerializer.Serialize(notification.Metadata);

            var entry = new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = notification.Action,
                TargetAccountId = notification.TargetAccountId,
                TargetEmail = Truncate(notification.TargetEmail, 256),
                ActorAccountId = actor,
                IsSuccess = notification.IsSuccess,
                Reason = Truncate(notification.Reason, MaxReasonLength),
                MetadataJson = metadataJson,
                IpAddress = ip,
                UserAgent = Truncate(userAgent, MaxUserAgentLength),
                DeviceId = deviceId,
                CorrelationId = correlationId
            };

            await _unitOfWork.AuditLogs.AddAsync(entry);
        }
        catch (Exception ex)
        {
            // KHÔNG throw — audit failure không được phá vỡ business flow.
            _logger.LogError(ex,
                "Failed to append audit log entry. Action={Action} TargetAccountId={TargetAccountId}",
                notification.Action, notification.TargetAccountId);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
