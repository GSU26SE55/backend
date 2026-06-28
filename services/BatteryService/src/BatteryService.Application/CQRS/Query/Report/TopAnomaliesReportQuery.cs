using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — top loại anomaly theo số lượng (kèm số Critical).</summary>
public class TopAnomaliesReportQuery : IRequest<CommonResponse<List<TopAnomalyRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Limit { get; set; } = 10;
}
