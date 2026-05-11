using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Session;

public class AdminRevokeAccountSessionsCommandHandler : IRequestHandler<AdminRevokeAccountSessionsCommand, SessionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public AdminRevokeAccountSessionsCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<SessionActionResponse> Handle(AdminRevokeAccountSessionsCommand request, CancellationToken cancellationToken)
    {
        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.AccountId, cancellationToken);

        if (!accountExists)
        {
            return new SessionActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
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

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AdminForceLogout, request.AccountId, IsSuccess: true,
            Reason: request.Reason,
            Metadata: new Dictionary<string, object?> { ["revokedSessions"] = sessions.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SessionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Admin đã thu hồi {sessions.Count} session.",
            Data = sessions.Count
        };
    }
}
