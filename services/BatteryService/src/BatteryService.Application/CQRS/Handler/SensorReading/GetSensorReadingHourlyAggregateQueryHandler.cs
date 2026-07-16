using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
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

    public GetSensorReadingHourlyAggregateQueryHandler(ISensorReadingAggregateViewReader reader)
    {
        _reader = reader;
    }

    public async Task<CommonResponse<List<SensorReadingAggregateDto>>> Handle(
        GetSensorReadingHourlyAggregateQuery request, CancellationToken cancellationToken)
    {
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
