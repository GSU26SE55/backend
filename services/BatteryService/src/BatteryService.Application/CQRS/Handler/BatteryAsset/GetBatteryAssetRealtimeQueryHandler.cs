using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetBatteryAssetRealtimeQueryHandler : IRequestHandler<GetBatteryAssetRealtimeQuery, CommonResponse<BatteryAssetRealtimeDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryAssetRealtimeQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryAssetRealtimeDto>> Handle(GetBatteryAssetRealtimeQuery request, CancellationToken cancellationToken)
    {
        var asset = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.Id && !item.IsDeleted, cancellationToken);

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
                AssetId = asset.Id,
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
