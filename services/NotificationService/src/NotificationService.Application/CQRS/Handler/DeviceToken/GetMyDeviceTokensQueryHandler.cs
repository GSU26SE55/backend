using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.DeviceToken;
using NotificationService.Application.DTOs.Response.DeviceToken;
using NotificationService.Application.Interfaces.Repositories;

namespace NotificationService.Application.CQRS.Handler.DeviceToken;

public class GetMyDeviceTokensQueryHandler : IRequestHandler<GetMyDeviceTokensQuery, DeviceTokenListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetMyDeviceTokensQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DeviceTokenListResponse> Handle(GetMyDeviceTokensQuery request, CancellationToken cancellationToken)
    {
        var items = await _unitOfWork.DeviceTokens.GetAllAsync()
            .Where(x => x.UserId == request.UserId && !x.IsDeleted)
            .AsNoTracking()
            .OrderByDescending(x => x.LastUsedAt ?? x.CreatedAt)
            .Select(x => new DeviceTokenDto
            {
                Id = x.Id,
                Platform = x.Platform,
                DeviceInfo = x.DeviceInfo,
                IsActive = x.IsActive,
                LastUsedAt = x.LastUsedAt,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new DeviceTokenListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
        };
    }
}
