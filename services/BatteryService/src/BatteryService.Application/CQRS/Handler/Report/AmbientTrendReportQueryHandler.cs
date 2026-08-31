using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class AmbientTrendReportQueryHandler
    : IRequestHandler<AmbientTrendReportQuery, CommonResponse<List<AmbientTrendPoint>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public AmbientTrendReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<AmbientTrendPoint>>> Handle(
        AmbientTrendReportQuery request, CancellationToken ct)
    {
        var to = request.To ?? DateTime.UtcNow;
        var from = request.From ?? to.AddDays(-30);

        var readings = await _uow.AmbientReadings.GetAllAsync().AsNoTracking()
            .Where(r => r.SiteId == request.SiteId && r.Time >= from && r.Time <= to)
            .Select(r => new { r.Time, r.AmbientTemperature, r.Humidity, r.SolarIrradiance })
            .ToListAsync(ct);

        var rows = readings
            .GroupBy(r => ReportBucket.Of(r.Time, request.Granularity))
            .Select(g => new AmbientTrendPoint
            {
                Date = g.Key,
                // AmbientTemperature nullable từ khi có báo cáo chỉ-có-gas (MQ-2, không qua SHT31)
                // — lọc HasValue trước khi Avg/Max/Min, giống HumidityAvg/IrradianceAvg bên dưới.
                AvgTemp = g.Any(x => x.AmbientTemperature.HasValue)
                    ? Math.Round(g.Where(x => x.AmbientTemperature.HasValue).Average(x => x.AmbientTemperature!.Value), 2)
                    : 0m,
                MaxTemp = g.Any(x => x.AmbientTemperature.HasValue)
                    ? g.Where(x => x.AmbientTemperature.HasValue).Max(x => x.AmbientTemperature!.Value)
                    : 0m,
                MinTemp = g.Any(x => x.AmbientTemperature.HasValue)
                    ? g.Where(x => x.AmbientTemperature.HasValue).Min(x => x.AmbientTemperature!.Value)
                    : 0m,
                HumidityAvg = g.Any(x => x.Humidity.HasValue)
                    ? Math.Round(g.Where(x => x.Humidity.HasValue).Average(x => x.Humidity!.Value), 2)
                    : null,
                IrradianceAvg = g.Any(x => x.SolarIrradiance.HasValue)
                    ? Math.Round(g.Where(x => x.SolarIrradiance.HasValue).Average(x => x.SolarIrradiance!.Value), 2)
                    : null
            })
            .OrderBy(p => p.Date)
            .ToList();

        return new CommonResponse<List<AmbientTrendPoint>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
