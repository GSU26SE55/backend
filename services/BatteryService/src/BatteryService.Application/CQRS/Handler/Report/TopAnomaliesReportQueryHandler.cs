using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class TopAnomaliesReportQueryHandler
    : IRequestHandler<TopAnomaliesReportQuery, CommonResponse<List<TopAnomalyRow>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public TopAnomaliesReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<TopAnomalyRow>>> Handle(
        TopAnomaliesReportQuery request, CancellationToken ct)
    {
        var limit = request.Limit <= 0 ? 10 : Math.Min(request.Limit, 100);
        var q = _uow.Alerts.GetAllAsync().AsNoTracking().Where(a => !a.IsDeleted);
        if (request.From.HasValue)
            q = q.Where(a => a.DetectedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(a => a.DetectedAt <= request.To.Value);

        var grouped = await q
            .GroupBy(a => a.AnomalyType)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count(),
                Critical = g.Count(x => x.Severity == AlertSeverityEnum.Critical)
            })
            .ToListAsync(ct);

        var rows = grouped
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .Select(x => new TopAnomalyRow
            {
                AnomalyType = x.Type.ToString(),
                Count = x.Count,
                CriticalCount = x.Critical
            })
            .ToList();

        return new CommonResponse<List<TopAnomalyRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
