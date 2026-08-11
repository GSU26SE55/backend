using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

/// <summary>
/// Sprint Bonus NS-06 (#650, PA-4) — trả min/max nạp/xả từ continuous aggregate 1h.
/// </summary>
public class GetSensorReadingHourlyAggregateQueryHandler
    : IRequestHandler<GetSensorReadingHourlyAggregateQuery, CommonResponse<List<SensorReadingAggregateDto>>>
{
    private readonly ISensorReadingAggregateViewReader _reader;
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSensorReadingHourlyAggregateQueryHandler(
        ISensorReadingAggregateViewReader reader,
        IBatteryUnitOfWork unitOfWork,
        IBatteryCurrentUserService currentUserService)
    {
        _reader = reader;
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<List<SensorReadingAggregateDto>>> Handle(
        GetSensorReadingHourlyAggregateQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — telemetry thuộc tenant qua asset; Customer chỉ đọc được asset của mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<List<SensorReadingAggregateDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Could not identify the current user."
            };
        }

        // 404 thay vì 403: không tiết lộ rằng asset của tenant khác có tồn tại.
        if (!await BatteryTenantAccessGuard.CanAccessAssetAsync(_unitOfWork, request.BatteryAssetId, scope, cancellationToken))
        {
            return new CommonResponse<List<SensorReadingAggregateDto>>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Battery asset not found."
            };
        }

        var items = await _reader.ReadHourlyAsync(
            request.BatteryAssetId, request.From, request.To, cancellationToken);

        return new CommonResponse<List<SensorReadingAggregateDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items.ToList()
        };
    }
}
