using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — histogram thời gian resolution.</summary>
public class ResolutionTimeHistogramReportQuery : IRequest<CommonResponse<List<HistogramBucketRow>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
