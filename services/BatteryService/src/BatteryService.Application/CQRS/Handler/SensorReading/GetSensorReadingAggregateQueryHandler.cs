using BatteryService.Application.Common;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingAggregateQueryHandler : IRequestHandler<GetSensorReadingAggregateQuery, CommonResponse<List<SensorReadingAggregateDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSensorReadingAggregateQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<List<SensorReadingAggregateDto>>> Handle(GetSensorReadingAggregateQuery request, CancellationToken cancellationToken)
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

        // Sprint Bonus NS-02 (#647) — chỉ tính trên reading source primary. Nếu không lọc, mỗi
        // giá trị bị đếm ~3 lần (BMS primary + INA226 redundant + DS18B20 external-temp mirror)
        // và AvgCurrent dính noise ±0.05A của INA226 tạo max giả.
        var primaryReadings = readings.Where(r => SensorSource.IsPrimary(r.SensorSourceCode));

        var result = primaryReadings
            .GroupBy(r => TruncateTime(ToUtc(r.Time), request.Interval))
            .OrderBy(g => g.Key)
            .Select(g => BuildBucket(g.Key, g.ToList()))
            .ToList();

        return new CommonResponse<List<SensorReadingAggregateDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = result
        };
    }

    /// <summary>
    /// Sprint Bonus NS-02 (#647) — build 1 bucket: AVG (giữ cũ, backward-compat) + min/max V/T +
    /// min/max/avg dòng tách 2 chiều nạp/xả. Quy ước: current &gt; 0 = nạp, current &lt; 0 = xả;
    /// min/max dòng LUÔN trả DƯƠNG; sample current == 0 (idle) không thuộc chiều nào (bỏ qua);
    /// field nullable = bucket không có mẫu chiều đó (không trả 0).
    /// </summary>
    private static SensorReadingAggregateDto BuildBucket(
        DateTime bucketTime, IReadOnlyList<Domain.Entities.SensorReading> bucket)
    {
        var charge = bucket.Where(r => r.Current > 0m).Select(r => r.Current).ToList();
        var discharge = bucket.Where(r => r.Current < 0m).Select(r => Math.Abs(r.Current)).ToList();

        return new SensorReadingAggregateDto
        {
            Time = bucketTime,
            AvgVoltage = Math.Round(bucket.Average(r => r.Voltage), 4),
            AvgCurrent = Math.Round(bucket.Average(r => r.Current), 4),
            AvgTemperature = Math.Round(bucket.Average(r => r.Temperature), 4),
            AvgSocPercent = Math.Round(bucket.Average(r => r.SocPercent), 4),
            AvgSohPercent = bucket.Any(r => r.SohPercent.HasValue)
                ? Math.Round(bucket.Where(r => r.SohPercent.HasValue).Average(r => r.SohPercent!.Value), 4)
                : null,

            MinVoltage = Math.Round(bucket.Min(r => r.Voltage), 4),
            MaxVoltage = Math.Round(bucket.Max(r => r.Voltage), 4),
            MinTemperature = Math.Round(bucket.Min(r => r.Temperature), 4),
            MaxTemperature = Math.Round(bucket.Max(r => r.Temperature), 4),

            MaxChargeCurrent = charge.Count > 0 ? Math.Round(charge.Max(), 4) : null,
            MinChargeCurrent = charge.Count > 0 ? Math.Round(charge.Min(), 4) : null,
            AvgChargeCurrent = charge.Count > 0 ? Math.Round(charge.Average(), 4) : null,
            MaxDischargeCurrent = discharge.Count > 0 ? Math.Round(discharge.Max(), 4) : null,
            MinDischargeCurrent = discharge.Count > 0 ? Math.Round(discharge.Min(), 4) : null,
            AvgDischargeCurrent = discharge.Count > 0 ? Math.Round(discharge.Average(), 4) : null,
            ChargeSampleCount = charge.Count,
            DischargeSampleCount = discharge.Count
        };
    }

    private static DateTime TruncateTime(DateTime utc, string interval) => interval switch
    {
        "1m" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc),
        "5m" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 5), 0, DateTimeKind.Utc),
        "15m" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 15), 0, DateTimeKind.Utc),
        "1h" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
        "1d" => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
        _ => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc)
    };

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
