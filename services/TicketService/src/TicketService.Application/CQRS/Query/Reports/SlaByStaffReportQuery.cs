using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — SLA compliance theo staff.</summary>
public class SlaByStaffReportQuery : IRequest<CommonResponse<List<SlaByStaffRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
