using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — chỉ số CSAT.</summary>
public class CsatReportQuery : IRequest<CommonResponse<CsatDto>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
