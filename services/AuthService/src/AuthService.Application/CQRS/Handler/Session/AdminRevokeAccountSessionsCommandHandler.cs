using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Session;

public class AdminRevokeAccountSessionsCommandHandler : IRequestHandler<AdminRevokeAccountSessionsCommand, SessionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly ITokenRevocationStore _revocationStore;

    public AdminRevokeAccountSessionsCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        ITokenRevocationStore revocationStore)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _revocationStore = revocationStore;
    }

    public async Task<SessionActionResponse> Handle(AdminRevokeAccountSessionsCommand request, CancellationToken cancellationToken)
    {
        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (!accountExists)
        {
            return new SessionActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Account not found."
            };
        }

        var sessions = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == request.AccountId && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Admin revoke" : $"Admin revoke: {request.Reason}";
        foreach (var session in sessions)
        {
            session.Status = RefreshTokenStatus.Revoked;
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = reason;
            _unitOfWork.RefreshTokens.UpdateAsync(session);
        }

        // #AUTH-54: bulk revoke access tokens — admin force-logout invalidate ngay tất cả access token.
        await _revocationStore.RevokeAllByAccountAsync(request.AccountId, TimeSpan.FromHours(1), cancellationToken);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AdminForceLogout, request.AccountId, IsSuccess: true,
            Reason: request.Reason,
            Metadata: new Dictionary<string, object?> { ["revokedSessions"] = sessions.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SessionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Admin revoked {sessions.Count} session(s).",
            Data = sessions.Count
        };
    }
}
