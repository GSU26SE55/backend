using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Services;

namespace AuthService.Application.CQRS.Handler.Session;

public class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, SessionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public RevokeSessionCommandHandler(IAuthUnitOfWork unitOfWork, ICurrentUserService currentUserService, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _publisher = publisher;
    }

    public async Task<SessionActionResponse> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
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

        var session = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .FirstOrDefaultAsync(rt => rt.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            return new SessionActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Session not found."
            };
        }

        if (session.AccountId != userId)
        {
            return new SessionActionResponse
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "You do not have permission to revoke this session."
            };
        }

        if (session.Status != RefreshTokenStatus.Active)
        {
            return new SessionActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Session is no longer active.",
                Data = 0
            };
        }

        session.Status = RefreshTokenStatus.Revoked;
        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = "User revoked";
        _unitOfWork.RefreshTokens.UpdateAsync(session);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.SessionRevoked, session.AccountId, true,
            Metadata: new Dictionary<string, object?> { ["sessionId"] = session.Id }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SessionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Session revoked successfully.",
            Data = 1
        };
    }
}
