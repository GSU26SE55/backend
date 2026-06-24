using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — hiệu suất staff.</summary>
public class StaffPerformanceReportQuery : IRequest<CommonResponse<List<StaffPerformanceRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
