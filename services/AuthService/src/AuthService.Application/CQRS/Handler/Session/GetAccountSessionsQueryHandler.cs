using AuthService.Application.CQRS.Query.Session;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Session;

public class GetAccountSessionsQueryHandler : IRequestHandler<GetAccountSessionsQuery, SessionListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAccountSessionsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SessionListResponse> Handle(GetAccountSessionsQuery request, CancellationToken cancellationToken)
    {
        if (request.AccountId == Guid.Empty)
        {
            return new SessionListResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Invalid AccountId."
            };
        }

        var query = _unitOfWork.RefreshTokens
            .GetAllAsync()
            .AsNoTracking()
            .Where(rt => rt.AccountId == request.AccountId);

        if (request.ActiveOnly)
            query = query.Where(rt => rt.Status == RefreshTokenStatus.Active && rt.ExpiredAt > DateTime.UtcNow);

        var sessions = await query
            .OrderByDescending(rt => rt.IssuedAt)
            .Select(rt => new SessionDto
            {
                Id = rt.Id,
                IssuedAt = rt.IssuedAt,
                ExpiredAt = rt.ExpiredAt,
                Status = rt.Status,
                IpAddress = rt.IpAddress,
                UserAgent = rt.UserAgent,
                DeviceId = rt.DeviceId,
                RevokedAt = rt.RevokedAt,
                RevokedReason = rt.RevokedReason
            })
            .ToListAsync(cancellationToken);

        return new SessionListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = sessions
        };
    }
}
