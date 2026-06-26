using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class EnvironmentalIncidentsReportQueryHandler
    : IRequestHandler<EnvironmentalIncidentsReportQuery, CommonResponse<List<EnvironmentalIncidentRow>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public EnvironmentalIncidentsReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<EnvironmentalIncidentRow>>> Handle(
        EnvironmentalIncidentsReportQuery request, CancellationToken ct)
    {
        var q = _uow.EnvironmentalIncidents.GetAllAsync().AsNoTracking().Where(i => !i.IsDeleted);
        if (request.From.HasValue)
            q = q.Where(i => i.DetectedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(i => i.DetectedAt <= request.To.Value);
        if (request.SiteId.HasValue)
            q = q.Where(i => i.SiteId == request.SiteId.Value);
        if (request.Type.HasValue)
            q = q.Where(i => (int)i.IncidentType == request.Type.Value);

        var incidents = await q
            .Select(i => new
            {
                i.SiteId,
                i.IncidentType,
                i.Severity,
                i.DetectedAt,
                i.ResolvedAt,
                i.FalseAlarmAt
            })
            .OrderByDescending(i => i.DetectedAt)
            .ToListAsync(ct);

        var rows = incidents.Select(i => new EnvironmentalIncidentRow
        {
            SiteId = i.SiteId.ToString(),
            IncidentType = i.IncidentType.ToString(),
            Severity = i.Severity.ToString(),
            DetectedAt = i.DetectedAt,
            ResolvedAt = i.ResolvedAt,
            DurationHours = i.ResolvedAt.HasValue
                ? Math.Round((decimal)(i.ResolvedAt.Value - i.DetectedAt).TotalHours, 2)
                : null,
            WasFalseAlarm = i.FalseAlarmAt != null
        }).ToList();

        return new CommonResponse<List<EnvironmentalIncidentRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
