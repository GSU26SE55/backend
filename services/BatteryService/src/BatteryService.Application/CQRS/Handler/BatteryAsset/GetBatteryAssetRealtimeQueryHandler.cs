using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetBatteryAssetRealtimeQueryHandler : IRequestHandler<GetBatteryAssetRealtimeQuery, CommonResponse<BatteryAssetRealtimeDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetBatteryAssetRealtimeQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<BatteryAssetRealtimeDto>> Handle(GetBatteryAssetRealtimeQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ được xem snapshot asset của chính mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<BatteryAssetRealtimeDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        var assetQuery = _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(item => item.Id == request.Id && !item.IsDeleted);

        // 404 thay vì 403: không tiết lộ rằng asset của tenant khác có tồn tại.
        if (scope.IsCustomerScoped)
        {
            assetQuery = assetQuery.Where(item => item.CustomerId == scope.CustomerId);
        }

        var asset = await assetQuery.FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
        {
            return new CommonResponse<BatteryAssetRealtimeDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài sản pin."
            };
        }

        var latest = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(reading => reading.BatteryAssetId == request.Id)
            .OrderByDescending(reading => reading.Time)
            .FirstOrDefaultAsync(cancellationToken);

        var activeAlerts = await _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .CountAsync(alert =>
                alert.BatteryAssetId == request.Id &&
                !alert.IsDeleted &&
                alert.Status != AlertStatusEnum.Resolved &&
                alert.Status != AlertStatusEnum.Merged, cancellationToken);

        return new CommonResponse<BatteryAssetRealtimeDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BatteryAssetRealtimeDto
            {
                AssetId = asset.Id.ToString(),
                SerialNumber = asset.SerialNumber,
                Status = asset.Status,
                Time = latest?.Time,
                Voltage = latest?.Voltage,
                Current = latest?.Current,
                Temperature = latest?.Temperature,
                SocPercent = latest?.SocPercent,
                CycleCount = latest?.CycleCount,
                SohPercent = latest?.SohPercent,
                ChargingState = latest?.ChargingState,
                ActiveAlerts = activeAlerts
            }
        };
    }
}
