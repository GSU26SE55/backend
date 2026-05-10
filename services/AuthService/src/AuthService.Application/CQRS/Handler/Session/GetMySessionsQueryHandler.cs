using AuthService.Application.CQRS.Query.Session;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Services;

namespace AuthService.Application.CQRS.Handler.Session;

public class GetMySessionsQueryHandler : IRequestHandler<GetMySessionsQuery, SessionListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public GetMySessionsQueryHandler(IAuthUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<SessionListResponse> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
        {
            return new SessionListResponse
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Chưa đăng nhập."
            };
        }

        var query = _unitOfWork.RefreshTokens
            .GetAllAsync()
            .AsNoTracking()
            .Where(rt => rt.AccountId == userId);

        if (request.ActiveOnly)
        {
            query = query.Where(rt => rt.Status == RefreshTokenStatus.Active && rt.ExpiredAt > DateTime.UtcNow);
        }

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
