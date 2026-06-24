using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — sự cố môi trường theo site/loại/thời gian.</summary>
public class EnvironmentalIncidentsReportQuery : IRequest<CommonResponse<List<EnvironmentalIncidentRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? SiteId { get; set; }
    public int? Type { get; set; }   // EnvironmentalIncidentTypeEnum value
}
