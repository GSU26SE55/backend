using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingHistoryQueryHandler : IRequestHandler<GetSensorReadingHistoryQuery, CommonResponse<PaginationResponse<SensorReadingDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSensorReadingHistoryQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<SensorReadingDto>>> Handle(GetSensorReadingHistoryQuery request, CancellationToken cancellationToken)
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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(reading => reading.Time)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(reading => new SensorReadingDto
            {
                Time = reading.Time,
                BatteryAssetId = reading.BatteryAssetId,
                Voltage = reading.Voltage,
                Current = reading.Current,
                Temperature = reading.Temperature,
                SocPercent = reading.SocPercent,
                CycleCount = reading.CycleCount,
                SourceDeviceId = reading.SourceDeviceId
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<SensorReadingDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<SensorReadingDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
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
