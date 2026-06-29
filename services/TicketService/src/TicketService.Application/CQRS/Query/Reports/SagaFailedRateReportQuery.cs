using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.CQRS.Query.Reports;

/// <summary>Sprint 7 #114 (§5.2) — saga failed rate theo thời gian (Admin only).</summary>
public class SagaFailedRateReportQuery : IRequest<CommonResponse<List<SagaFailedRatePoint>>>
{
    public DateTime? From { get; set; }
    /// <summary>
    /// Thời gian kết thúc lọc (UTC).
    /// </summary>
    public DateTime? To { get; set; }
    public string Granularity { get; set; } = "day";
}
