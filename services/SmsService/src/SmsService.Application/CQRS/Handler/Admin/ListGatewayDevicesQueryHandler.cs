using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Query.Admin;
using SmsService.Application.DTOs.Response.Admin;
using SmsService.Application.Interfaces.Repositories;

namespace SmsService.Application.CQRS.Handler.Admin;

public class ListGatewayDevicesQueryHandler
    : IRequestHandler<ListGatewayDevicesQuery, CommonResponse<List<GatewayDeviceDto>>>
{
    private readonly ISmsUnitOfWork _unitOfWork;

    public ListGatewayDevicesQueryHandler(ISmsUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<GatewayDeviceDto>>> Handle(
        ListGatewayDevicesQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.SmsGatewayDevices
            .GetAllAsync()
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!request.IncludeRevoked)
            query = query.Where(x => x.IsActive);

        var data = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new GatewayDeviceDto(
                x.Id,
                x.DeviceName,
                x.DeviceCode,
                x.IsActive,
                x.RevokedAt,
                x.DailyLimit,
                x.SentToday,
                x.SentTodayDate,
                x.LastSeenAt,
                x.LastSeenIp,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<GatewayDeviceDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OK",
            Data = data
        };
    }
}
