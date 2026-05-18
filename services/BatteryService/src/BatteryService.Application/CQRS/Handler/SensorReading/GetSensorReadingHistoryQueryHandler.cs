using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingHistoryQueryHandler : IRequestHandler<GetSensorReadingHistoryQuery, CommonResponse<SensorReadingHistoryResponseDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSensorReadingHistoryQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SensorReadingHistoryResponseDto>> Handle(GetSensorReadingHistoryQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(reading => reading.BatteryAssetId == request.BatteryAssetId);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(reading => reading.Time >= from);
        }

        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(reading => reading.Time <= to);
        }

        if (request.Cursor.HasValue)
        {
            var cursor = ToUtc(request.Cursor.Value);
            query = query.Where(reading => reading.Time < cursor);
        }

        var limit = Math.Clamp(request.Limit, 1, GetSensorReadingHistoryQuery.MaxLimit);
        var page = await query
            .OrderByDescending(reading => reading.Time)
            .Take(limit + 1)
            .Select(reading => new SensorReadingDto
            {
                Time = reading.Time,
                BatteryAssetId = reading.BatteryAssetId.ToString(),
                Voltage = reading.Voltage,
                Current = reading.Current,
                Temperature = reading.Temperature,
                SocPercent = reading.SocPercent,
                CycleCount = reading.CycleCount,
                SourceDeviceId = reading.SourceDeviceId
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var items = hasMore ? page.Take(limit).ToList() : page;

        return new CommonResponse<SensorReadingHistoryResponseDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new SensorReadingHistoryResponseDto
            {
                Items = items,
                NextCursor = hasMore ? items[^1].Time : null,
                HasMore = hasMore
            }
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
