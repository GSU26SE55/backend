using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — SLA compliance theo priority.</summary>
public class SlaByPriorityReportQuery : IRequest<CommonResponse<List<SlaByPriorityRow>>>
{
    public DateTime? From { get; set; }
    /// <summary>
    /// Thời gian kết thúc lọc (UTC).
    /// </summary>
    public DateTime? To { get; set; }
}
