using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — xu hướng nhiệt độ môi trường theo thời gian cho 1 site.</summary>
public class AmbientTrendReportQuery : IRequest<CommonResponse<List<AmbientTrendPoint>>>
{
    public Guid SiteId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Granularity { get; set; } = "day";
}
