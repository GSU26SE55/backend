using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class AlertVolumeReportQueryHandler
    : IRequestHandler<AlertVolumeReportQuery, CommonResponse<List<ReportTimeSeriesPoint>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public AlertVolumeReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<ReportTimeSeriesPoint>>> Handle(
        AlertVolumeReportQuery request, CancellationToken ct)
    {
        var to = request.To ?? DateTime.UtcNow;
        var from = request.From ?? to.AddDays(-30);

        var dates = await _uow.Alerts.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted && a.DetectedAt >= from && a.DetectedAt <= to)
            .Select(a => a.DetectedAt)
            .ToListAsync(ct);

        var rows = dates
            .GroupBy(d => ReportBucket.Of(d, request.Granularity))
            .Select(g => new ReportTimeSeriesPoint { Date = g.Key, Count = g.Count() })
            .OrderBy(p => p.Date)
            .ToList();

        return new CommonResponse<List<ReportTimeSeriesPoint>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
