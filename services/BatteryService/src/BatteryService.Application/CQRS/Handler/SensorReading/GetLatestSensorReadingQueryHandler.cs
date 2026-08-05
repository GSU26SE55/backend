using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetLatestSensorReadingQueryHandler : IRequestHandler<GetLatestSensorReadingQuery, CommonResponse<SensorReadingDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetLatestSensorReadingQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<SensorReadingDto>> Handle(GetLatestSensorReadingQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — telemetry thuộc tenant qua asset; Customer chỉ đọc được asset của mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<SensorReadingDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        // 404 thay vì 403: không tiết lộ rằng asset của tenant khác có tồn tại.
        if (!await BatteryTenantAccessGuard.CanAccessAssetAsync(_unitOfWork, request.BatteryAssetId, scope, cancellationToken))
        {
            return new CommonResponse<SensorReadingDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài sản pin."
            };
        }

        var entity = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(reading => reading.BatteryAssetId == request.BatteryAssetId)
            .OrderByDescending(reading => reading.Time)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<SensorReadingDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy sensor reading."
            };
        }

        return new CommonResponse<SensorReadingDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
