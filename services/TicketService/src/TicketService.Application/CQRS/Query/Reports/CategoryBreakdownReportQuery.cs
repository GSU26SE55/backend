using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — phân bố ticket theo category.</summary>
public class CategoryBreakdownReportQuery : IRequest<CommonResponse<List<CategoryBreakdownRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
