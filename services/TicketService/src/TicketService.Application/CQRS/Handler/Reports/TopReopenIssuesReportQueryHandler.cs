using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Reports;

public class TopReopenIssuesReportQueryHandler
    : IRequestHandler<TopReopenIssuesReportQuery, CommonResponse<List<ReopenIssueRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public TopReopenIssuesReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<ReopenIssueRow>>> Handle(TopReopenIssuesReportQuery request, CancellationToken ct)
    {
        var limit = request.Limit <= 0 ? 10 : Math.Min(request.Limit, 100);

        var data = await _uow.Tickets.GetAllAsync().AsNoTracking()
            .Where(t => !t.IsDeleted && t.ReopenCount > 0)
            .Select(t => new { t.Category, t.ReopenCount }).ToListAsync(ct);

        var rows = data.GroupBy(x => x.Category).Select(g => new ReopenIssueRow
        {
            Category = g.Key.ToString(),
            Count = g.Count(),
            AvgReopenCount = Math.Round((decimal)g.Average(x => x.ReopenCount), 2)
        }).OrderByDescending(r => r.Count).Take(limit).ToList();

        return new CommonResponse<List<ReopenIssueRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
