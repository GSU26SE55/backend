using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — top issue bị reopen.</summary>
public class TopReopenIssuesReportQuery : IRequest<CommonResponse<List<ReopenIssueRow>>>
{
    public int Limit { get; set; } = 10;
}
