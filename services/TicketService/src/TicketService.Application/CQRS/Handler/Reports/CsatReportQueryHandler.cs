using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Reports;

public class CsatReportQueryHandler
    : IRequestHandler<CsatReportQuery, CommonResponse<CsatDto>>
{
    private readonly ITicketUnitOfWork _uow;
    public CsatReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<CsatDto>> Handle(CsatReportQuery request, CancellationToken ct)
    {
        var q = _uow.Tickets.GetAllAsync().AsNoTracking().Where(t => !t.IsDeleted && t.Rating != null);
        if (request.From.HasValue)
            q = q.Where(t => t.RatedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(t => t.RatedAt <= request.To.Value);

        var ratings = await q.Select(t => (int)t.Rating!.Value).ToListAsync(ct);

        var dto = new CsatDto
        {
            TotalRated = ratings.Count,
            AvgRating = ratings.Count == 0 ? 0m : Math.Round((decimal)ratings.Average(), 2),
            RatingDistribution = Enumerable.Range(1, 5).ToDictionary(s => s, s => ratings.Count(r => r == s))
        };

        return new CommonResponse<CsatDto> { IsSuccess = true, StatusCode = 200, Data = dto };
    }
}
