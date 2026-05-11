using AuthService.Application.Configuration;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.Application.CQRS.Handler.Session;

/// <summary>
/// Enforce concurrent session limit per account theo FIFO:
/// - Đếm session đã persisted (active) của user.
/// - Nếu (persistedCount + 1 new session) > MaxConcurrentSessions →
///   revoke (excess) session CŨ NHẤT theo IssuedAt asc.
///
/// Không throw nếu fail — limit enforcement là best-effort.
/// </summary>
public class SessionCreatedNotificationHandler : INotificationHandler<SessionCreatedNotification>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly SessionOptions _options;
    private readonly ILogger<SessionCreatedNotificationHandler> _logger;

    public SessionCreatedNotificationHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IOptions<SessionOptions> options,
        ILogger<SessionCreatedNotificationHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Handle(SessionCreatedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var maxAllowed = _options.MaxConcurrentSessions;
            if (maxAllowed <= 0)
                return; // 0 hoặc âm = tắt limit

            // Đếm session ĐÃ PERSIST (chưa tính session mới vừa Add trong cùng DbContext).
            var persistedActive = await _unitOfWork.RefreshTokens
                .GetAllAsync()
                .Where(rt => rt.AccountId == notification.AccountId
                             && rt.Status == RefreshTokenStatus.Active)
                .OrderBy(rt => rt.IssuedAt)
                .ToListAsync(cancellationToken);

            // +1 cho session mới vừa Add (chưa SaveChanges nhưng đã trong DbContext ChangeTracker).
            var totalAfterCommit = persistedActive.Count + 1;
            if (totalAfterCommit <= maxAllowed)
                return;

            var revokeCount = totalAfterCommit - maxAllowed;
            var toRevoke = persistedActive.Take(revokeCount).ToList();
            var now = DateTime.UtcNow;
            var reason = $"Concurrent session limit ({maxAllowed}) exceeded — revoked oldest session.";

            foreach (var session in toRevoke)
            {
                session.Status = RefreshTokenStatus.Revoked;
                session.RevokedAt = now;
                session.RevokedReason = reason;
                _unitOfWork.RefreshTokens.UpdateAsync(session);
            }

            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.SessionLimitExceededOldestRevoked, notification.AccountId, IsSuccess: true,
                Metadata: new Dictionary<string, object?>
                {
                    ["revokedCount"] = revokeCount,
                    ["maxAllowed"] = maxAllowed,
                    ["revokedSessionIds"] = toRevoke.Select(s => s.Id.ToString()).ToList()
                }), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enforce session limit for AccountId={AccountId}",
                notification.AccountId);
        }
    }
}
