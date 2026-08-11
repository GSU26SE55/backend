using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Services;

namespace AuthService.Application.CQRS.Handler.Session;

public class RevokeAllSessionsCommandHandler : IRequestHandler<RevokeAllSessionsCommand, SessionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public RevokeAllSessionsCommandHandler(IAuthUnitOfWork unitOfWork, ICurrentUserService currentUserService, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _publisher = publisher;
    }

    public async Task<SessionActionResponse> Handle(RevokeAllSessionsCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
        {
            return new SessionActionResponse
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Not logged in."
            };
        }

        var query = _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == userId && rt.Status == RefreshTokenStatus.Active);

        if (request.ExceptCurrent && !string.IsNullOrWhiteSpace(request.CurrentRefreshToken))
        {
            // #AUTH-01: DB chỉ lưu hash → so sánh "trừ token hiện tại" cũng phải qua hash.
            var currentHash = RefreshTokenHasher.Hash(request.CurrentRefreshToken);
            query = query.Where(rt => rt.Token != currentHash);
        }

        var sessions = await query.ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Status = RefreshTokenStatus.Revoked;
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = "Revoke all sessions";
            _unitOfWork.RefreshTokens.UpdateAsync(session);
        }

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AllSessionsRevoked, userId, true,
            Metadata: new Dictionary<string, object?> { ["count"] = sessions.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SessionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Revoked {sessions.Count} session(s).",
            Data = sessions.Count
        };
    }
}
