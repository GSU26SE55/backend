using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — số lượng Alert theo thời gian (mặc định 30 ngày, granularity=day).</summary>
public class AlertVolumeReportQuery : IRequest<CommonResponse<List<ReportTimeSeriesPoint>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Granularity { get; set; } = "day";
}
