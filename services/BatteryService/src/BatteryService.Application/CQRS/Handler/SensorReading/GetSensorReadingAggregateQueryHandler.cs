using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingAggregateQueryHandler : IRequestHandler<GetSensorReadingAggregateQuery, CommonResponse<List<SensorReadingAggregateDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSensorReadingAggregateQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<SensorReadingAggregateDto>>> Handle(GetSensorReadingAggregateQuery request, CancellationToken cancellationToken)
    {
        var baseQuery = _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(r => r.BatteryAssetId == request.BatteryAssetId);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            baseQuery = baseQuery.Where(r => r.Time >= from);
        }

        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            baseQuery = baseQuery.Where(r => r.Time <= to);
        }

        // Materialize filtered readings, then bucket in memory.
        // EF.Functions.DateTruncate requires Npgsql (Infrastructure-only package),
        // so bucketing is done client-side. Dashboard queries are bounded (max ~7d),
        // keeping row counts manageable.
        var readings = await baseQuery.ToListAsync(cancellationToken);

        var result = readings
            .GroupBy(r => TruncateTime(ToUtc(r.Time), request.Interval))
            .OrderBy(g => g.Key)
            .Select(g => new SensorReadingAggregateDto
            {
                Time = g.Key,
                AvgVoltage = Math.Round(g.Average(r => r.Voltage), 4),
                AvgCurrent = Math.Round(g.Average(r => r.Current), 4),
                AvgTemperature = Math.Round(g.Average(r => r.Temperature), 4),
                AvgSocPercent = Math.Round(g.Average(r => r.SocPercent), 4),
                AvgSohPercent = g.Any(r => r.SohPercent.HasValue)
                    ? Math.Round(g.Where(r => r.SohPercent.HasValue).Average(r => r.SohPercent!.Value), 4)
                    : null
            })
            .ToList();

        return new CommonResponse<List<SensorReadingAggregateDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = result
        };
    }

    private static DateTime TruncateTime(DateTime utc, string interval) => interval switch
    {
        "1m"  => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc),
        "5m"  => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 5), 0, DateTimeKind.Utc),
        "15m" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 15), 0, DateTimeKind.Utc),
        "1h"  => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
        "1d"  => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
        _     => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc)
    };

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
